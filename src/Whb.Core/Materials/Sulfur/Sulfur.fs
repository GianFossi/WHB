namespace Whb.Core

open ROP
open ROP.Returns.Operators
open WhbThermo.Domain

/// Thin Whb.Core facade over XSulfur.
///
/// This module converts units and naming to the conventions already used in
/// Whb.Core. Sulfur chemistry, thermodynamics, liquid properties and film
/// kinetics remain implemented in XSulfur only.
module SulfurAdapter =

    let private modelResult = lazy (XSulfur.Speciation.load ())

    let model () = modelResult.Force()

    let private toBar (pPa: float) = pPa / 1.0e5 * 1.0<bar>
    let private toPa (p: float<bar>) = float p * 1.0e5

    let dewPoint (pSulfurPa: float) =
        model ()
        >>= fun m ->
            XSulfur.Chemistry.dewPoint m (toBar pSulfurPa)
            >>= fun t -> Returns.ok (float t)

    let vapourPressure (tK: float) =
        model ()
        >>= fun m ->
            XSulfur.Chemistry.vapourPressure m (tK * 1.0<K>)
            >>= fun p -> Returns.ok (toPa p)

    let condenserState (tK: float) (inletSulfurPa: float) =
        model ()
        >>= fun m -> XSulfur.Chemistry.condenserState m (tK * 1.0<K>) (toBar inletSulfurPa)

    let speciation (tK: float) (pSulfurPa: float) =
        model ()
        >>= fun m -> XSulfur.Speciation.distribution m (tK * 1.0<K>) (toBar pSulfurPa)

    let effectiveHeatCapacity (tK: float) (pSulfurPa: float) =
        model ()
        >>= fun m -> XSulfur.Speciation.effectiveHeatCapacity m (tK * 1.0<K>) (toBar pSulfurPa)

    let heatOfCondensation (tK: float) (pSulfurPa: float) =
        model ()
        >>= fun m -> XSulfur.Condensation.heatOfCondensation m (tK * 1.0<K>) (toBar pSulfurPa)

    let liquidViscosity (tK: float) = XSulfur.LiquidProperties.viscosity (tK * 1.0<K>)
    let liquidDensity (tK: float) = XSulfur.LiquidProperties.density (tK * 1.0<K>)
    let liquidConductivity (tK: float) = XSulfur.LiquidProperties.conductivity (tK * 1.0<K>)
    let liquidHeatCapacity (tK: float) = XSulfur.LiquidProperties.heatCapacity (tK * 1.0<K>)
    let liquidSurfaceTension (tK: float) = XSulfur.LiquidProperties.surfaceTension (tK * 1.0<K>)
    let viscosityPenalty (tK: float) = XSulfur.LiquidProperties.viscosityPenalty (tK * 1.0<K>)

    let film (tWallK: float) (tSatK: float) (rhoVapour: float) (hfg: float) (dInt: float) =
        XSulfur.FilmKinetics.nusseltHorizontalFilm (tWallK * 1.0<K>) (tSatK * 1.0<K>)
                                                   rhoVapour hfg dInt

    let drainage (condensateFlow: float) (dInt: float) (length: float) (slope: float)
                 (gasVelocity: float) (gasDensity: float) (tFilmK: float) =
        XSulfur.FilmKinetics.drainage condensateFlow dInt length slope gasVelocity
                                      gasDensity (tFilmK * 1.0<K>)

    let drainageVerdict (filmState: XSulfur.FilmKinetics.FilmState)
                        (drainageState: XSulfur.FilmKinetics.DrainageState)
                        (tFilmK: float) =
        XSulfur.FilmKinetics.assess filmState drainageState (tFilmK * 1.0<K>)

    let steamPressureForWall (tWallK: float) = Steam.psat_MPa tWallK * 1.0e6

    let wallWindow (tWallK: float) = XSulfur.Checks.wallWindow (tWallK * 1.0<K>)
    let sulfidation (tWallK: float) (yH2S: float) = XSulfur.Checks.sulfidation (tWallK * 1.0<K>) yH2S
    let wetH2S (tMetalK: float) (tWaterDewK: float) (yH2S: float) =
        XSulfur.Checks.wetH2S (tMetalK * 1.0<K>) (tWaterDewK * 1.0<K>) yH2S

    let condensationActive (tGasK: float) (pSulfurPa: float) =
        model ()
        >>= fun m -> XSulfur.Checks.condensationActive m (tGasK * 1.0<K>) (toBar pSulfurPa)

    let assessFog = XSulfur.Checks.assessFog
    let fogCheck = XSulfur.Checks.fogCheck
    let drainageCheck = XSulfur.Checks.drainageCheck
    let worst = XSulfur.Checks.worst

    let condenserChecks (tWallK: float) (tGasK: float) (pSulfurPa: float) (yH2S: float)
                        (fog: XSulfur.Checks.FogAssessment)
                        (drainageResult: XSulfur.FilmKinetics.DrainageVerdict) =
        model ()
        >>= fun m ->
            XSulfur.Checks.condenserPoint m (tWallK * 1.0<K>) (tGasK * 1.0<K>)
                                          (toBar pSulfurPa) yH2S fog drainageResult
