namespace Whb.Core

open Types

module CirculationContracts =
    type Distribution =
        { WExtLin: float[]
          WFieldLin: float[]
          WBypLin: float[]
          XInField: float[]
          /// Sign changes of the loop balance over its bracket. More than one means the
          /// operating point is not unique.
          RootCount: int
          /// False when the bracket held no sign change at all, so the reported flow is a
          /// clamped endpoint rather than a solution.
          BracketOk: bool
          /// Slope of the loop balance at the operating point. Negative is stable (Ledinegg).
          BalanceSlope: float
          Global: CirculationResult }
