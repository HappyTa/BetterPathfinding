using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using Verse.AI;

namespace BetterPathfinding
{
	/*******************************
	PathDataLog saves/loads (almost) all of the state needed for a pathfinding call
	This minimizes the amount of game state needed to run test cases, allowing for
	perfectly repeatable automated testing.
	*******************************/
	public class PathDataLog : IExposable
	{
		private static ScribeSaver saver = Scribe.saver;
		private static ScribeLoader loader = Scribe.loader;

		public static void SaveFromPathCall(Map map, IntVec3 startVec, LocalTargetInfo dest, TraverseParms traverseParms, PathEndMode peMode)
		{
			CellRect destinationRect;
			if (dest.HasThing && peMode != PathEndMode.OnCell)
			{
				destinationRect = dest.Thing.OccupiedRect();
			}
			else
			{
				destinationRect = CellRect.SingleCell(dest.Cell);
			}

			var dumper = new PathDataLog
			{
				mapSize = map.Size,
				start = startVec,
				dest = destinationRect,
				peMode = peMode,
				tpMode = traverseParms.mode,
				tpMaxDanger = traverseParms.maxDanger,
				tpCanBashDoors = traverseParms.canBashDoors,
				tpCanBashFences = traverseParms.canBashFences,
				tpMoveCardinal = traverseParms.pawn?.TicksPerMoveCardinal ?? -1f,
				tpMoveDiagonal = traverseParms.pawn?.TicksPerMoveDiagonal ?? -1f,
				pathGrid = map.pathing.For(traverseParms).pathGrid.pathGrid,
				fakeEdificeGrid = new ByteGrid(map),
				avoidGrid = traverseParms.pawn?.GetAvoidGrid(),
				allowedArea = traverseParms.pawn?.playerSettings?.AreaRestrictionInPawnCurrentMap
		};

			foreach (var cell in map.AllCells)
			{
				var rb = cell.GetRegionBarrier(map);
				int value = Edifice_None;
				if (rb != null)
				{
					var door = rb as Building_Door;
					if (door != null)
					{
						switch (traverseParms.mode)
						{
							case TraverseMode.ByPawn:
								if (!traverseParms.canBashDoors && door.IsForbiddenToPass(traverseParms.pawn)) { value = Edifice_NonTraversableDoor; }
								else if (!door.FreePassage) { value = door.PawnCanOpen(traverseParms.pawn) ? door.TicksToOpenNow : Edifice_NonTraversableDoor; }
								else
								{ value = 0; }
								break;
							case TraverseMode.NoPassClosedDoors:
								value = !door.FreePassage ? Edifice_NonTraversableDoor : 0;
								break;
						}
					}
					else if ((rb as Building)?.PathFindCostFor(traverseParms.pawn) > 0) { value = Edifice_KnownArmedTrap; }
					else
					{ value = Edifice_Impassible; }
				}
				dumper.fakeEdificeGrid[cell] = (byte) value;
			}

			var savePath = Path.Combine(GenFilePaths.DevOutputFolderPath, $"{traverseParms.pawn} - {Find.TickManager.TicksAbs}");
			if (File.Exists(savePath + ".xml"))
			{
				savePath = savePath + " ";
				int saveNum = 1;
				while (File.Exists(savePath + saveNum + ".xml")) { saveNum++; }
				savePath = savePath + saveNum;
			}
			try
			{
				SaveGame(savePath);
				try
				{
					saver.InitSaving(savePath + ".xml", "PathDataLog");
				}
				catch (Exception ex)
				{
					GenUI.ErrorDialog("Stuff went wrong " + ex);
					throw;
				}
				ScribeMetaHeaderUtility.WriteMetaHeader();

				Scribe_Deep.Look(ref dumper, "PathData");
			}
			catch (Exception ex2)
			{
				Log.Error("Exception while saving: " + ex2);
			}
			finally
			{
				saver.FinalizeSaving();
			}

		}

		public static PathDataLog LoadFromFile(string filename)
		{
			var pathData = new PathDataLog();

			try
			{
				loader.InitLoading(filename);
				ScribeMetaHeaderUtility.LoadGameDataHeader(ScribeMetaHeaderUtility.ScribeHeaderMode.Map, false);
				Scribe.EnterNode("PathData");
				pathData.ExposeData();
				Scribe.ExitNode();
			}
			catch (Exception e)
			{
				Log.Error("Exception while loading: " + e);
			}
			finally
			{
				// done loading
				loader.FinalizeLoading();
				Scribe.mode = LoadSaveMode.Inactive;
			}

			return pathData;
		}

		private static void SaveGame(string savePath)
		{
			LongEventHandler.QueueLongEvent(() =>
			{
				string path = savePath + ".rws";
				SafeSaver.Save(path, "savegame", delegate
				{
					ScribeMetaHeaderUtility.WriteMetaHeader();
					Game game = Current.Game;
					Scribe_Deep.Look(ref game, "game");
				});
			}, "Saving Path Data", false, null);
		}

		public const int Edifice_None = 255;
		public const int Edifice_Impassible = 254;
		public const int Edifice_NonTraversableDoor = 253;
		public const int Edifice_KnownArmedTrap = 250;


		public IntVec3 mapSize;
		public IntVec3 start;
		public CellRect dest;
		public PathEndMode peMode;
		public TraverseMode tpMode;
		public Danger tpMaxDanger;
		public bool tpCanBashDoors;
		public bool tpCanBashFences;
		public float tpMoveCardinal;
		public float tpMoveDiagonal;
		public ByteGrid avoidGrid;
		public Area allowedArea;
		public int[] pathGrid;
		public ByteGrid fakeEdificeGrid;

		public void ExposeData()
		{
			Scribe_Values.Look(ref mapSize, "mapSize");
			Scribe_Values.Look(ref start, "start");
			Scribe_Values.Look(ref dest, "dest");
			Scribe_Values.Look(ref peMode, "peMode");
			Scribe_Values.Look(ref tpMode, "tpMode");
			Scribe_Values.Look(ref tpMaxDanger, "tpMaxDanger");
			Scribe_Values.Look(ref tpCanBashDoors, "tpCanBash");
			Scribe_Values.Look(ref tpMoveCardinal, "tpMoveCardinal");
			Scribe_Values.Look(ref tpMoveDiagonal, "tpMoveDiagonal");
			Scribe_Deep.Look(ref fakeEdificeGrid, "fakeEdificeGrid");
			Scribe_Deep.Look(ref avoidGrid, "avoidGrid");
			Scribe_Deep.Look(ref allowedArea, "allowedArea");

			string compressedString = string.Empty;
			if (Scribe.mode == LoadSaveMode.Saving)
			{
				compressedString = IntArrayToCompressedString(pathGrid);
			}
			Scribe_Values.Look(ref compressedString, "pathGrid");
			if (Scribe.mode == LoadSaveMode.LoadingVars)
			{
				pathGrid = CompressedStringToIntArray(compressedString);
			}

		}

		private static string IntArrayToCompressedString(int[] array)
		{
			var bytes = new byte[array.Length * 4];
			for (var i = 0; i < array.Length; i++)
			{
				var intBytes = BitConverter.GetBytes(array[i]);
				for (var j = 0; j < 4; j++)
				{
					bytes[i * 4 + j] = intBytes[j];
				}
			}
			string str = Convert.ToBase64String(bytes);
			return ArrayExposeUtility.AddLineBreaksToLongString(str);
		}

		private static int[] CompressedStringToIntArray(string compressedString)
		{
			compressedString = ArrayExposeUtility.RemoveLineBreaks(compressedString);
			byte[] byteGrid = Convert.FromBase64String(compressedString);
			var ints = new int[byteGrid.Length / 4];
			for (var i = 0; i < ints.Length; i++)
			{
				ints[i] = BitConverter.ToInt32(byteGrid, i * 4);
			}
			return ints;
		}
	}
}
