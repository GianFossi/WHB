module WhbThermo.Tests.If97Regions35Tests

open Xunit
open Ganfoss.ROP
open WhbThermo.Domain
open WhbThermo.Steam

let private model =
    match If97.load () with
    | Success (m, _) -> m
    | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

let private near (expected: float) (relTol: float) (actual: float) (label: string) =
    Assert.True(abs (actual - expected) <= relTol * abs expected,
                $"{label}: expected {expected}, got {actual}")

/// IF97 Table 33: the region 3 basic equation at (rho, T).
[<Fact>]
let ``region 3 reproduces the IF97 verification table`` () =
    for (rho, t, p, h, s, cp) in
        [ 500.0, 650.0, 25.5837018, 1863.43019, 4.05427273, 13.8935717
          200.0, 650.0, 22.2930643, 2375.12401, 4.85438792, 44.6579342
          500.0, 750.0, 78.3095639, 2258.68845, 4.46971906, 6.34165359 ] do
        let (pOut, hOut, cpOut, sOut) = Water.region3 rho t
        near p 1e-8 pOut $"P({rho}, {t})"
        near h 1e-8 hOut $"h({rho}, {t})"
        near s 1e-8 sOut $"s({rho}, {t})"
        near cp 1e-8 cpOut $"cp({rho}, {t})"

/// IF97 Table 42: region 5.
[<Fact>]
let ``region 5 reproduces the IF97 verification table`` () =
    for (t, pMPa, v, h, s, cp) in
        [ 1500.0, 0.5, 1.38455090, 5219.76855, 9.65408875, 2.61609445
          1500.0, 30.0, 0.0230761299, 5167.23514, 7.72970133, 2.72724317
          2000.0, 30.0, 0.0311385219, 6571.22604, 8.53640523, 2.88569882 ] do
        match If97.properties model (t * 1.0<K>) (pMPa * 10.0<bar>) with
        | Success (st, _) ->
            Assert.Equal(5, st.Region)
            near v 1e-8 st.SpecificVolume $"v({t}, {pMPa})"
            near h 1e-8 st.Enthalpy $"h({t}, {pMPa})"
            near s 1e-8 st.Entropy $"s({t}, {pMPa})"
            near cp 1e-8 st.Cp $"cp({t}, {pMPa})"
        | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

/// Solving p(rho, T) = P for the density recovers the table state exactly.
[<Fact>]
let ``region 3 density solve inverts the basic equation`` () =
    match If97.properties model 650.0<K> (25.583701818521472 * 10.0<bar>) with
    | Success (st, _) -> near 500.0 1e-10 (float st.Density) "rho"
    | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

/// (P, T) states on both sides of B23 and both sides of saturation, against the
/// iapws package (its region 3 densities come from the backward equations,
/// hence 1e-5).
[<Fact>]
let ``region selection and region 3 states match an independent implementation`` () =
    for (t, pMPa, region, rho, h) in
        [ 650.0, 25.0, 3, 488.87505207909487, 1876.3591225169687
          750.0, 50.0, 3, 309.93308531305985, 2536.422362386141
          640.0, 20.0, 3, 160.5778870015766, 2452.457482210603
          700.0, 30.0, 2, 184.1801687597407, 2631.4947448448074
          630.0, 17.0, 2, 109.10542025413977, 2614.98865858571 ] do
        match If97.properties model (t * 1.0<K>) (pMPa * 10.0<bar>) with
        | Success (st, _) ->
            Assert.Equal(region, st.Region)
            near rho 1e-5 (float st.Density) $"rho({t}, {pMPa})"
            near h 1e-5 st.Enthalpy $"h({t}, {pMPa})"
        | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

/// Saturation above 623.15 K (16.53 MPa) lies in region 3, for both APIs.
/// Psat is the region 4 equation, and each saturated density must reproduce it
/// through the region 3 basic equation. The iapws package serves as the
/// independent comparison; its saturated states come from backward equations
/// and sit a few 1e-5 off Psat, which near the critical point (645 K) moves the
/// density by a few 1e-4 - hence the tolerance that widens towards Tc.
[<Fact>]
let ``saturation above 623 K uses region 3`` () =
    for (t, p, rhoL, rhoV, hL, hV, tol) in
        [ 625.0, 16.908097331064948, 567.0605971978611, 118.30746954258527, 1686.2747273734471, 2550.6558818982767, 1e-5
          630.0, 17.969098460753173, 544.3272924774005, 132.89359907400006, 1730.692165509592, 2510.7852492347774, 1e-5
          640.0, 20.265942167297563, 481.61228755118617, 177.40023664001458, 1841.9839057600252, 2394.4197850071632, 1e-4
          645.0, 21.51413929196862, 422.5692951873155, 225.0177335130036, 1934.4705452216092, 2279.9730262276585, 1e-3 ] do
        let s = Water.satT t
        near p 1e-12 (s.P / 1e6) $"Psat({t})"
        let (pL, _, _, _) = Water.region3 s.RhoL t
        let (pV, _, _, _) = Water.region3 s.RhoV t
        near p 1e-10 pL $"p(rhoL, {t})"
        near p 1e-10 pV $"p(rhoV, {t})"
        near rhoL tol s.RhoL $"rhoL({t})"
        near rhoV tol s.RhoV $"rhoV({t})"
        near (hL * 1000.0) tol s.HL $"hL({t})"
        near (hV * 1000.0) tol s.HV $"hV({t})"
        match If97.saturatedAt model (p * 10.0<bar>) with
        | Success (sat, _) ->
            Assert.Equal(3, sat.Liquid.Region)
            near s.RhoL 1e-9 (float sat.Liquid.Density) $"checked rhoL({t})"
            // Phase equilibrium: the Gibbs energies of the two phases agree to the
            // consistency of the region 4 and region 3 equations.
            let gL = sat.Liquid.Enthalpy - t * sat.Liquid.Entropy
            let gV = sat.Vapour.Enthalpy - t * sat.Vapour.Entropy
            Assert.True(abs (gL - gV) < 0.05, $"g_L - g_V = {gL - gV} kJ/kg at {t} K")
        | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

/// The switch from regions 1/2 to region 3 at 623.15 K is continuous to the
/// consistency IF97 guarantees across its region boundaries.
[<Fact>]
let ``saturation properties are continuous across 623.15 K`` () =
    let below = Water.satT 623.149
    let above = Water.satT 623.151
    near below.HL 1e-3 above.HL "hL"
    near below.HV 1e-3 above.HV "hV"
    near below.RhoL 1e-3 above.RhoL "rhoL"
    near below.RhoV 1e-3 above.RhoV "rhoV"
