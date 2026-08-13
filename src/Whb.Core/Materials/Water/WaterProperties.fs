namespace Whb.Core.MaterialData.Water

open Whb.Core

module WaterProperties =

    [<CLIMutable>]
    type SaturatedWaterPoint =
        { Pressure: float
          Temperature: float
          RhoLiquid: float
          RhoVapor: float
          Hfg: float
          MuLiquid: float
          ConductivityLiquid: float
          SurfaceTension: float }

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
