namespace Whb.Core.Loads

/// <summary>
/// Provides loads functionality for the WHB calculation model.
/// </summary>
/// <remarks>
/// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
/// </remarks>
module Loads =

    /// <summary>
    /// Represents loadsource data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type LoadSource = ClientPds | VendorPds | Calculated | Manual

    [<CLIMutable>]
    /// <summary>
    /// Represents thermalload data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
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
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type LoadSet =
        { Name: string; Loads: ThermalLoad list }
