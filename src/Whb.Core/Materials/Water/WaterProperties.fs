namespace Whb.Core.MaterialData.Water

open Whb.Core

/// <summary>
/// Provides waterproperties functionality for the WHB calculation model.
/// </summary>
/// <remarks>
/// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
/// </remarks>
module WaterProperties =

    [<CLIMutable>]
    /// <summary>
    /// Represents saturatedwaterpoint data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type SaturatedWaterPoint =
        { Pressure: float
          Temperature: float
          RhoLiquid: float
          RhoVapor: float
          Hfg: float
          MuLiquid: float
          ConductivityLiquid: float
          SurfaceTension: float }

    /// <summary>
    /// Calculates or returns saturated for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let saturated pressure =
        let s = Steam.sat pressure
        { Pressure = pressure
          Temperature = s.Tsat
          RhoLiquid = s.RhoL
          RhoVapor = s.RhoV
          Hfg = s.Hfg
          MuLiquid = s.MuL
          ConductivityLiquid = s.KL
          SurfaceTension = s.Sigma }
