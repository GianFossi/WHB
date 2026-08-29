namespace Whb.Core

open System
open Constants
open Types

module BundleSolverFoundation =
    let ferruleResistance (f: Ferrule) (di: float) (tMeanC: float) =
        if not f.Enabled then 0.0
        else
            let rSleeve =
                if f.SleeveOd > f.Bore then log (f.SleeveOd / f.Bore) / (2.0 * Math.PI * f.SleeveK tMeanC)
                else 0.0
            let rIns =
                if di > f.SleeveOd then log (di / f.SleeveOd) / (2.0 * Math.PI * f.InsulK tMeanC)
                else 0.0
            rSleeve + rIns

    let ferruleInsulationThickness (f: Ferrule) (di: float) =
        if not f.Enabled then 0.0 else max 0.0 (0.5 * (di - f.SleeveOd))

    let ferruleInsulationFitStatus (f: Ferrule) (di: float) =
        if not f.Enabled then "NOT INSTALLED"
        elif f.Bore <= 0.0 || f.SleeveOd <= 0.0 || di <= 0.0 then "CHECK - invalid ferrule geometry"
        elif f.Bore >= f.SleeveOd then "CHECK - bore is not smaller than sleeve OD"
        elif f.SleeveOd >= di then "CHECK - no radial space for insulation paper"
        else
            let thk = ferruleInsulationThickness f di
            if thk < 0.0005 then "CHECK - insulation paper thinner than 0.5 mm"
            elif thk > 0.003 then "CHECK - insulation paper thicker than 3 mm"
            else "OK"

    /// <summary>
    /// Estimates the gas-side pressure drop across the ferrule component.
    /// </summary>
    /// <remarks>
    /// The estimate includes friction through the smaller ferrule bore and the local expansion from ferrule bore to tube ID.
    /// </remarks>
    let ferrulePressureDropEstimate
        (f: Ferrule)
        (tubeDi: float)
        (roughness: float)
        (mdotPerTube: float)
        (props: GasProps.MixProps)
        (length: float)
        =
        if not f.Enabled || length <= 0.0 then 0.0
        else
            let bore = max 1e-6 f.Bore
            let area = Math.PI * bore * bore / 4.0
            let gFlux = mdotPerTube / area
            let velocity = gFlux / props.Rho
            let re = gFlux * bore / props.Mu
            let fDarcy = GasSide.darcyFriction re (roughness / bore)
            let dpFriction = GasSide.dpFrictionPerM fDarcy bore props.Rho velocity * length
            let kExpansion = (1.0 - (bore / tubeDi) ** 2.0) ** 2.0
            let dpExpansion = GasSide.dpLocal kExpansion props.Rho velocity
            dpFriction + dpExpansion

    let ferruleClasses (f: Ferrule) =
        if not f.Enabled || f.Lengths.IsEmpty then [ (1.0, 0.0) ]
        else
            let s = f.Lengths |> List.sumBy fst
            if s <= 0.0 then [ (1.0, 0.0) ] else f.Lengths |> List.map (fun (a, b) -> (a / s, b))

    /// <summary>
    /// Part of the shell-side coefficient that is independent of the local heat flux.
    /// </summary>
    /// <remarks>
    /// The Chen convective term, the Chen suppression factor and the bundle factor depend
    /// only on quality and cross-flow mass flux, both fixed within a cell. Evaluating them
    /// once per cell instead of once per heat-flux iteration leaves the coefficient
    /// unchanged term by term.
    /// </remarks>
    let shellContext (case: DesignCase) (sat: Steam.SatProps) (x: float) (gCross: float) : BundleSolverContracts.ShellContext =
        let t = case.Tube
        let d = t.Do
        let gl = gCross * (1.0 - x)
        let reMax = max 10.0 (gl * d / sat.MuL)
        let hLo = WaterSide.hZukauskas reMax sat.PrL sat.PrL sat.KL d t.Staggered (1.0 / 0.8660254)
        let fChen = WaterSide.chenF x sat
        { BundleFactor = WaterSide.bundleFactor case.Water.BundleFactor
          Suppression = WaterSide.chenS reMax fChen
          HConvChen = hLo * fChen
          HLo = hLo
          GCross = gCross
          X = x }

    let shellHtcWith (case: DesignCase) (sat: Steam.SatProps) (ctx: BundleSolverContracts.ShellContext) (qOut: float) =
        let d = case.Tube.Do
        let wc = case.Water
        match wc.FlowBoiling with
        | WaterSide.ChenSuperposition ->
            let hnb = WaterSide.hPool wc.Correlation qOut d sat wc.RoughnessUm wc.Csf
            let hnc = WaterSide.hNaturalConvection d (max 1.0 (qOut / 5000.0)) sat
            hnb * ctx.BundleFactor * ctx.Suppression + max ctx.HConvChen hnc
        | WaterSide.KandlikarMax ->
            (WaterSide.hKandlikar ctx.HLo qOut ctx.GCross ctx.X case.Tube.Do false 1.0 sat).HTp

    let shellHtc (case: DesignCase) (sat: Steam.SatProps) (qOut: float) (x: float) (gCross: float) =
        shellHtcWith case sat (shellContext case sat x gCross) qOut
