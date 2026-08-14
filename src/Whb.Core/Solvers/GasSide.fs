namespace Whb.Core

open System

/// <summary>
/// Provides gas-side forced-convection, radiation, and pressure-drop correlations for WHB thermal calculations.
/// </summary>
/// <remarks>
/// Uses empirical forced-convection and friction correlations for gas-side thermal calculations. Check Reynolds and Prandtl ranges, roughness assumptions, radiation settings, and SI units before applying results outside the reference WHB design envelope.
/// </remarks>
module GasSide =
    let fBlasius (re: float) = 0.3164 * Math.Pow(re, -0.25)
    let fFilonenko (re: float) =
        let x = 1.82 * log10 re - 1.64
        1.0 / (x * x)
    let fColebrook (re: float) (relRough: float) =
        let mutable f = fFilonenko re
        let mutable i = 0
        let mutable running = true
        while running && i < 40 do
            let rhs = -2.0 * log10 (relRough / 3.7 + 2.51 / (re * sqrt f))
            let fNext = 1.0 / (rhs * rhs)
            // The iteration is a contraction: stop as soon as it stops moving in
            // double precision instead of always paying the full 40 sweeps.
            running <- abs (fNext - f) > 1e-15 * abs fNext
            f <- fNext
            i <- i + 1
        f
    let fLaminar (re: float) = 64.0 / re
    let darcyFriction (re: float) (relRough: float) =
        if re < 2300.0 then fLaminar (max 1.0 re)
        elif relRough > 1e-6 then fColebrook re relRough
        else fFilonenko re
    type Correlation =
        | DittusBoelter
        | SiederTate
        | Colburn
        | Gnielinski
        | PetukhovKirillov
        | Hausen
    let correlationName =
        function
        | DittusBoelter -> "Dittus-Boelter (1930)"
        | SiederTate -> "Sieder-Tate (1936)"
        | Colburn -> "Colburn (1933)"
        | Gnielinski -> "Gnielinski (1976)"
        | PetukhovKirillov -> "Petukhov-Kirillov (1958)"
        | Hausen -> "Hausen (1959)"
    let nusseltFD (corr: Correlation) (re: float) (pr: float) (muRatio: float) =
        let ret = max re 1.0
        match corr with
        | DittusBoelter -> 0.023 * Math.Pow(ret, 0.8) * Math.Pow(pr, 0.3)
        | Colburn -> 0.023 * Math.Pow(ret, 0.8) * Math.Pow(pr, 1.0 / 3.0)
        | SiederTate ->
            0.027 * Math.Pow(ret, 0.8) * Math.Pow(pr, 1.0 / 3.0) * Math.Pow(muRatio, 0.14)
        | Gnielinski ->
            if ret < 2300.0 then 3.66
            else
                let f = fFilonenko ret
                let reEff = if ret < 4000.0 then ret else ret
                (f / 8.0) * (reEff - 1000.0) * pr
                / (1.0 + 12.7 * sqrt (f / 8.0) * (Math.Pow(pr, 2.0 / 3.0) - 1.0))
        | PetukhovKirillov ->
            let f = fFilonenko ret
            (f / 8.0) * ret * pr
            / (1.07 + 12.7 * sqrt (f / 8.0) * (Math.Pow(pr, 2.0 / 3.0) - 1.0))
        | Hausen ->
            if ret < 2300.0 then 3.66
            elif ret < 1.0e4 then
                0.116 * (Math.Pow(ret, 2.0 / 3.0) - 125.0) * Math.Pow(pr, 1.0 / 3.0)
                * Math.Pow(muRatio, 0.14)
            else 0.023 * Math.Pow(ret, 0.8) * Math.Pow(pr, 0.4)
    let gasPropertyCorrection (tWallK: float) (tBulkK: float) =
        let r = max 0.2 (min 5.0 (tWallK / tBulkK))
        Math.Pow(r, -0.5)
    let entranceCorrection (x: float) (d: float) (c: float) =
        let xe = max x (2.0 * d)
        1.0 + c * Math.Pow(d / xe, 0.7)
    let entranceCorrectionMean (l: float) (d: float) (c: float) =
        1.0 + c * Math.Pow(d / l, 0.7)
    type GasHtcResult =
        { Re: float
          Pr: float
          NuFD: float
          Nu: float
          HConv: float      // W/(m²·K)
          HRad: float       // W/(m²·K)
          HTot: float       // W/(m²·K)
          EpsGas: float
          Velocity: float   // m/s
          MassFlux: float } // kg/(m²·s)
    let localHtc
        (corr: Correlation)
        (props: GasProps.MixProps)
        (muWall: float)
        (di: float)
        (mdotPerTube: float)
        (x: float)                 // Axial distance from the inlet [m]
        (entranceC: float)
        (tWallK: float)
        (rH2O: float)
        (rCO2: float)
        (epsWall: float)
        (radiationOn: bool)
        : GasHtcResult =

        let a = Math.PI * di * di / 4.0
        let gFlux = mdotPerTube / a
        let vel = gFlux / props.Rho
        let re = gFlux * di / props.Mu
        let pr = props.Pr
        let muRatio = if muWall > 0.0 then props.Mu / muWall else 1.0
        let nuFD = nusseltFD corr re pr muRatio
        let fEnt = entranceCorrection x di entranceC
        let fProp =
            match corr with
            | SiederTate | Hausen -> 1.0          // Already included through (mub/muw)^0.14
            | _ -> gasPropertyCorrection tWallK props.T
        let nu = nuFD * fEnt * fProp
        let hConv = nu * props.K / di
        let sBeam = 0.9 * di
        let eps = if radiationOn then GasProps.gasEmissivity rH2O rCO2 props.P sBeam props.T else 0.0
        let hRad = if radiationOn then GasProps.hRadiation eps epsWall props.T tWallK else 0.0
        { Re = re; Pr = pr; NuFD = nuFD; Nu = nu
          HConv = hConv; HRad = hRad; HTot = hConv + hRad
          EpsGas = eps; Velocity = vel; MassFlux = gFlux }
    let dpFrictionPerM (f: float) (di: float) (rho: float) (vel: float) =
        f / di * rho * vel * vel / 2.0
    let dpLocal (k: float) (rho: float) (vel: float) = k * rho * vel * vel / 2.0




