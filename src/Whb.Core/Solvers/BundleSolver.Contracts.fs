namespace Whb.Core

open Types

module BundleSolverContracts =
    [<Struct>]
    type ShellContext =
        { BundleFactor: float
          Suppression: float
          HConvChen: float
          HLo: float
          GCross: float
          X: float }

    type SolveOutput =
        { Cells: CellResult[,,]
          Axial: AxialResult list
          Duty: float
          Steam: float
          DpGas: float
          SteamLin: float[]
          TGasOutBandClass: float[,]
          NTubesBand: float[]
          Classes: (float * float) list
          Dz: float[]
          ZC: float[]
          /// Cells whose outlet quality hit the 0.95 barrier, and where the first one was.
          QualityClamped: int
          QualityClampFirstZ: float
          /// Cells whose wall-temperature fixed point was still moving at the iteration cap.
          NonConvergedCells: int
          /// Duty raised in each vertical band, integrated over the tube length [W].
          BandDuty: float[]
          OutletCompositionBandClass: GasProps.Composition[,]
          SulphurCoupling: Sulphur.CouplingSummary option }
