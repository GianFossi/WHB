namespace Whb.Core

open System

/// Correlazioni di scambio termico e perdita di carico LATO GAS
/// (flusso interno al tubo, gas monofase, tubo liscio circolare).
module GasSide =

    // ------------------------------------------------------------------
    // Fattori d'attrito
    // ------------------------------------------------------------------
    /// Blasius (liscio, 3e3 < Re < 1e5): f = 0.3164 Re^-0.25 (Darcy)
    let fBlasius (re: float) = 0.3164 * Math.Pow(re, -0.25)

    /// Filonenko / Petukhov (liscio, 3e3 < Re < 5e6), Darcy
    let fFilonenko (re: float) =
        let x = 1.82 * log10 re - 1.64
        1.0 / (x * x)

    /// Colebrook-White implicito (tubo rugoso), Darcy
    let fColebrook (re: float) (relRough: float) =
        let mutable f = fFilonenko re
        for _ in 1 .. 40 do
            let rhs = -2.0 * log10 (relRough / 3.7 + 2.51 / (re * sqrt f))
            f <- 1.0 / (rhs * rhs)
        f

    /// Fattore d'attrito laminare
    let fLaminar (re: float) = 64.0 / re

    let darcyFriction (re: float) (relRough: float) =
        if re < 2300.0 then fLaminar (max 1.0 re)
        elif relRough > 1e-6 then fColebrook re relRough
        else fFilonenko re

    // ------------------------------------------------------------------
    // Correlazioni di Nusselt
    // ------------------------------------------------------------------
    type Correlation =
        /// Nu = 0.023 Re^0.8 Pr^0.3  (raffreddamento del fluido)
        | DittusBoelter
        /// Nu = 0.027 Re^0.8 Pr^(1/3) (µb/µw)^0.14
        | SiederTate
        /// Nu = 0.023 Re^0.8 Pr^(1/3)
        | Colburn
        /// Gnielinski (1976) con f di Filonenko - raccomandata
        | Gnielinski
        /// Petukhov-Kirillov (1958)
        | PetukhovKirillov
        /// Hausen per regime di transizione
        | Hausen

    let correlationName =
        function
        | DittusBoelter -> "Dittus-Boelter (1930)"
        | SiederTate -> "Sieder-Tate (1936)"
        | Colburn -> "Colburn (1933)"
        | Gnielinski -> "Gnielinski (1976)"
        | PetukhovKirillov -> "Petukhov-Kirillov (1958)"
        | Hausen -> "Hausen (1959)"

    /// Nusselt completamente sviluppato.
    ///   re, pr : numeri di Reynolds e Prandtl al bulk
    ///   muRatio: µ_bulk/µ_wall (per Sieder-Tate)
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

    /// Correzione per proprietà variabili nei gas (Kays & London):
    /// Nu/Nu_cp = (Tw/Tb)^n con n = -0.5 in raffreddamento del gas.
    let gasPropertyCorrection (tWallK: float) (tBulkK: float) =
        let r = max 0.2 (min 5.0 (tWallK / tBulkK))
        Math.Pow(r, -0.5)

    /// Correzione di ingresso termico/idrodinamico (tubo con imbocco netto).
    /// Nu(x)/Nu_fd = 1 + C (d/x)^0.7   con C ≈ 1.4 per imbocco a spigolo vivo.
    /// La correlazione e' valida per x/d >~ 2: sotto tale limite il rapporto
    /// viene congelato al valore in x = 2d (altrimenti diverge per x -> 0).
    let entranceCorrection (x: float) (d: float) (c: float) =
        let xe = max x (2.0 * d)
        1.0 + c * Math.Pow(d / xe, 0.7)

    /// Correzione integrale di ingresso su tutta la lunghezza L (per verifica).
    let entranceCorrectionMean (l: float) (d: float) (c: float) =
        1.0 + c * Math.Pow(d / l, 0.7)

    // ------------------------------------------------------------------
    // Coefficiente locale completo lato gas
    // ------------------------------------------------------------------
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

    /// Calcolo del coefficiente locale lato gas comprensivo di radiazione.
    let localHtc
        (corr: Correlation)
        (props: GasProps.MixProps)
        (muWall: float)
        (di: float)
        (mdotPerTube: float)
        (x: float)                 // ascissa dall'ingresso [m]
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
            | SiederTate | Hausen -> 1.0          // già inclusa via (µb/µw)^0.14
            | _ -> gasPropertyCorrection tWallK props.T
        let nu = nuFD * fEnt * fProp
        let hConv = nu * props.K / di
        let sBeam = 0.9 * di
        let eps = if radiationOn then GasProps.gasEmissivity rH2O rCO2 props.P sBeam props.T else 0.0
        let hRad = if radiationOn then GasProps.hRadiation eps epsWall props.T tWallK else 0.0
        { Re = re; Pr = pr; NuFD = nuFD; Nu = nu
          HConv = hConv; HRad = hRad; HTot = hConv + hRad
          EpsGas = eps; Velocity = vel; MassFlux = gFlux }

    /// Perdita di carico distribuita per unità di lunghezza [Pa/m]
    let dpFrictionPerM (f: float) (di: float) (rho: float) (vel: float) =
        f / di * rho * vel * vel / 2.0

    /// Perdite localizzate: ingresso in tubo (K≈0.5), uscita (K≈1.0),
    /// ferrula (contrazione + espansione).
    let dpLocal (k: float) (rho: float) (vel: float) = k * rho * vel * vel / 2.0
