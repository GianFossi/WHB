namespace Whb.Core.Loads

/// <summary>
/// Defines load inputs and load-combination data used by WHB process and mechanical calculations.
/// </summary>
/// <remarks>
/// Defines process and mechanical load data used by WHB thermal, hydraulic, and structural screening calculations. Keep units, design cases, and load-combination assumptions aligned with the governing project basis.
/// </remarks>
module Loads =
    type LoadSource = ClientPds | VendorPds | Calculated | Manual

    [<CLIMutable>]
    type ThermalLoad =
        { Tag: string
          Source: LoadSource
          GasMassFlow: float
          GasInletTemperature: float
          GasInletPressure: float
          TargetOutletTemperature: float option
          Duty: float option
          Notes: string }

    [<CLIMutable>]
    type LoadSet =
        { Name: string; Loads: ThermalLoad list }




