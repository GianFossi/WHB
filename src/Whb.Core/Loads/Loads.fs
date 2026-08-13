namespace Whb.Core.Loads

/// <summary>
/// Defines load inputs and load-combination data used by WHB process and mechanical calculations.
/// </summary>
/// <remarks>
/// Defines process and mechanical load data used by WHB thermal, hydraulic, and structural screening calculations. Keep units, design cases, and load-combination assumptions aligned with the governing project basis.
/// </remarks>
module Loads =

    /// <summary>
    /// Represents loadsource data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Defines process and mechanical load data used by WHB thermal, hydraulic, and structural screening calculations. Keep units, design cases, and load-combination assumptions aligned with the governing project basis.
    /// </remarks>
    type LoadSource = ClientPds | VendorPds | Calculated | Manual

    [<CLIMutable>]
    /// <summary>
    /// Represents thermalload data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Defines process and mechanical load data used by WHB thermal, hydraulic, and structural screening calculations. Keep units, design cases, and load-combination assumptions aligned with the governing project basis.
    /// </remarks>
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
    /// <summary>
    /// Represents loadset data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Defines process and mechanical load data used by WHB thermal, hydraulic, and structural screening calculations. Keep units, design cases, and load-combination assumptions aligned with the governing project basis.
    /// </remarks>
    type LoadSet =
        { Name: string; Loads: ThermalLoad list }


