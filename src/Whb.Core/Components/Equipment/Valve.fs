namespace Whb.Core

open System
open Constants

/// <summary>
/// Provides valve functionality for the WHB calculation model.
/// </summary>
/// <remarks>
/// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
/// </remarks>
module Valve =

    let private table =
        [ 0.0,  0.20
          5.0,  0.24
          10.0, 0.52
          15.0, 0.90
          20.0, 1.54
          25.0, 2.51
          30.0, 3.91
          35.0, 6.22
          40.0, 10.8
          45.0, 18.7
          50.0, 32.6
          55.0, 58.8
          60.0, 118.0
          65.0, 256.0
          70.0, 751.0 ]

    let private arr = table |> List.toArray

    /// <summary>
    /// Calculates or returns zetaclosure for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let zetaClosure (alphaDeg: float) =
        let a = max 0.0 alphaDeg
        if a <= 0.0 then snd arr.[0]
        elif a >= fst arr.[arr.Length - 1] then
            let (a1, z1) = arr.[arr.Length - 2]
            let (a2, z2) = arr.[arr.Length - 1]
            let s = (log z2 - log z1) / (a2 - a1)
            min 1.0e7 (z2 * exp (s * (a - a2)))
        else
            let mutable i = 0
            while i < arr.Length - 2 && fst arr.[i + 1] < a do i <- i + 1
            let (a1, z1) = arr.[i]
            let (a2, z2) = arr.[i + 1]
            exp (log z1 + (log z2 - log z1) * (a - a1) / (a2 - a1))

    /// <summary>
    /// Calculates or returns zetaopening for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let zetaOpening (openDeg: float) = zetaClosure (90.0 - openDeg)

    /// <summary>
    /// Calculates or returns closureforzeta for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let closureForZeta (z: float) =
        let zz = max (snd arr.[0]) z
        let n = arr.Length
        if zz >= snd arr.[n - 1] then
            let (a1, z1) = arr.[n - 2]
            let (a2, z2) = arr.[n - 1]
            let s = (log z2 - log z1) / (a2 - a1)
            min 90.0 (a2 + log (zz / z2) / s)
        else
            let mutable i = 0
            while i < n - 2 && snd arr.[i + 1] < zz do i <- i + 1
            let (a1, z1) = arr.[i]
            let (a2, z2) = arr.[i + 1]
            a1 + (a2 - a1) * (log zz - log z1) / (log z2 - log z1)

    /// <summary>
    /// Calculates or returns openingforzeta for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let openingForZeta (z: float) = 90.0 - closureForZeta z

    /// <summary>
    /// Calculates or returns zetaflatdisc for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let zetaFlatDisc (thicknessRatio: float) (closureDeg: float) =
        let a = max 0.0 (min 89.9 closureDeg) * Math.PI / 180.0
        let sigma =
            max 1e-4 (1.0 - sin a - 4.0 * thicknessRatio / Math.PI * cos a)
        let cc = 0.62 + 0.38 * sigma * sigma * sigma
        let r = 1.0 / (cc * sigma) - 1.0
        r * r + 0.20

    /// <summary>
    /// Calculates or returns zetaflatdisccalibrated for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let zetaFlatDiscCalibrated (thicknessRatio: float) (closureDeg: float) =
        0.82 * (zetaFlatDisc thicknessRatio closureDeg - 0.20) + 0.20

    /// <summary>
    /// Calculates or returns cvfromzeta for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let cvFromZeta (idM: float) (zeta: float) =
        let dIn = idM / 0.0254
        29.9 * dIn * dIn / sqrt (max 1e-9 zeta)

    /// <summary>
    /// Calculates or returns kvfromzeta for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let kvFromZeta (idM: float) (zeta: float) = cvFromZeta idM zeta / 1.156

    /// <summary>
    /// Calculates or returns kvrequired for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let kvRequired (wKgS: float) (rho: float) (dpPa: float) =
        let w = wKgS * 3600.0
        w / sqrt (1000.0 * max 1e-6 rho * max 1e-9 (dpPa / 1.0e5))

    /// <summary>
    /// Calculates or returns pressuredropratio for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let pressureDropRatio (dpPa: float) (p1Pa: float) = dpPa / p1Pa

    /// <summary>
    /// Calculates or returns gain for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let gain (openDeg: float) =
        let d = 0.5
        let z1 = zetaOpening (openDeg - d)
        let z2 = zetaOpening (openDeg + d)
        (log z1 - log z2) / (2.0 * d)

    /// <summary>
    /// Calculates or returns throatvelocity for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let throatVelocity (dp: float) (rho: float) = sqrt (2.0 * max 0.0 dp / max 1e-6 rho)

    /// <summary>
    /// Calculates or returns sonic for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let sonic (gamma: float) (mw: float) (tK: float) = sqrt (gamma * R * tK / mw)
