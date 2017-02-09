﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using Verse.AI;

namespace BetterPathfinding
{
	public class PathDataDumper : IExposable
	{
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

			var dumper = new PathDataDumper
			{
				mapSize = map.Size,
				start = startVec,
				dest = destinationRect,
				peMode = peMode,
				tpMode = traverseParms.mode,
				tpMaxDanger = traverseParms.maxDanger,
				tpCanBash = traverseParms.canBash,
				tpMoveCardinal = traverseParms.pawn?.TicksPerMoveCardinal ?? -1,
				tpMoveDiagonal = traverseParms.pawn?.TicksPerMoveDiagonal ?? -1,
				pathGrid = map.pathGrid.pathGrid.ToList(),
				fakeEdificeGrid = new ByteGrid(map)
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
								if (!traverseParms.canBash && door.IsForbiddenToPass(traverseParms.pawn)) { value = Edifice_NonTraversableDoor; }
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
				try
				{
					Scribe.InitWriting(savePath + ".xml", "PathDataLog");
				}
				catch (Exception ex)
				{
					GenUI.ErrorDialog("Stuff went wrong " + ex);
					throw;
				}
				ScribeMetaHeaderUtility.WriteMetaHeader();

				Scribe_Deep.LookDeep(ref dumper, "PathData");
			}
			catch (Exception ex2)
			{
				Log.Error("Exception while saving: " + ex2);
			}
			finally
			{
				Scribe.FinalizeWriting();
			}

		}

		public static PathDataDumper LoadFromFile(string filename)
		{
			var pathData = new PathDataDumper();

			try
			{
				Scribe.InitLoading(filename);
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
				Scribe.FinalizeLoading();
				Scribe.mode = LoadSaveMode.Inactive;
			}

			return pathData;
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

		public bool tpCanBash;

		public int tpMoveCardinal;

		public int tpMoveDiagonal;

		//TODO: avoidGrid

		//TODO: allowed area


		//Map info

		public List<int> pathGrid;

		public ByteGrid fakeEdificeGrid;


		public void ExposeData()
		{
			Scribe_Values.LookValue(ref mapSize, "mapSize");
			Scribe_Values.LookValue(ref start, "start");
			Scribe_Values.LookValue(ref dest, "dest");
			Scribe_Values.LookValue(ref peMode, "peMode");
			Scribe_Values.LookValue(ref tpMode, "tpMode");
			Scribe_Values.LookValue(ref tpMaxDanger, "tpMaxDanger");
			Scribe_Values.LookValue(ref tpCanBash, "tpCanBash");
			Scribe_Values.LookValue(ref tpMoveCardinal, "tpMoveCardinal");
			Scribe_Values.LookValue(ref tpMoveDiagonal, "tpMoveDiagonal");
			Scribe_Collections.LookList(ref pathGrid, "pathGrid");
			Scribe_Deep.LookDeep(ref fakeEdificeGrid, "fakeEdificeGrid");
		}
	}
}
