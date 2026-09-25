namespace WhbThermo.Properties

open System
open Ganfoss.ROP
open WhbThermo.Domain

/// Multicomponent mixing rules: Wilke (viscosity), Wassiljewa with the
/// Mason & Saxena modification (conductivity), mass-weighted Cp.
module Mixing =

    /// Wilke interaction parameter.
    ///   phi_ij = [1 + (mu_i/mu_j)^0.5 (M_j/M_i)^0.25]^2 / [8 (1 + M_i/M_j)]^0.5
    ///
    /// (M_j/M_i)^0.25 is evaluated with Math.Pow, as Whb.Core always did; the
    /// earlier sqrt(sqrt(.)) form differed only in the last bit.
    ///
    /// Used for viscosity (Wilke) and, with the same phi, for conductivity
    /// (Wassiljewa). Validated against Cantera's Wilke implementation to
    /// 0.0000 % on the mixtures in tests/reference.
    ///
    /// The design manual names the "Mason & Saxena modification", whose epsilon
    /// factor of 1.065 multiplies the interaction term. It is deliberately NOT
    /// applied here: the genuine Mason-Saxena form uses the ratio of monatomic
    /// translational conductivities rather than of viscosities, and putting
    /// epsilon on top of this viscosity-ratio phi pushes the mixture
    /// conductivity below the rigorous lower bound 1/SUM(x_i/k_i) in about half
    /// of the test mixtures. See MixtureGoldenTests.
    let wilkePhi (mI: float) (mJ: float) (muI: float) (muJ: float) =
        let a = 1.0 + sqrt (muI / muJ) * Math.Pow(mJ / mI, 0.25)
        a * a / sqrt (8.0 * (1.0 + mI / mJ))

    let private phi (muI: float) (muJ: float) (mI: float) (mJ: float) = wilkePhi mI mJ muI muJ

    /// Wilke viscosity and Wassiljewa conductivity of a mixture from plain arrays
    /// of mole fractions, molar masses, pure viscosities and conductivities, in
    /// any consistent units. The denominator of component i is built once and
    /// shared by both properties; a non-positive denominator drops that
    /// component instead of dividing by it. This is the kernel the WHB solver
    /// calls on every cell, so it allocates nothing.
    let wilkeWassiljewa (y: float[]) (m: float[]) (mu: float[]) (k: float[]) : struct (float * float) =
        let n = y.Length
        let mutable muAcc = 0.0
        let mutable kAcc = 0.0
        for i in 0 .. n - 1 do
            let mutable d = 0.0
            for j in 0 .. n - 1 do
                d <- d + y.[j] * wilkePhi m.[i] m.[j] mu.[i] mu.[j]
            muAcc <- muAcc + (if d <= 0.0 then 0.0 else y.[i] * mu.[i] / d)
            kAcc <- kAcc + (if d <= 0.0 then 0.0 else y.[i] * k.[i] / d)
        struct (muAcc, kAcc)

    /// Simple averages used to compare with datasheets that quote them: mole
    /// fraction average for viscosity, sqrt(M)-weighted average for conductivity.
    let molarAverage (y: float[]) (m: float[]) (mu: float[]) (k: float[]) : struct (float * float) =
        let n = y.Length
        let mutable sw = 0.0
        let mutable muAcc = 0.0
        let mutable kAcc = 0.0
        for i in 0 .. n - 1 do
            sw <- sw + y.[i] * sqrt m.[i]
        for i in 0 .. n - 1 do
            muAcc <- muAcc + y.[i] * mu.[i]
        for i in 0 .. n - 1 do
            kAcc <- kAcc + y.[i] * sqrt m.[i] * k.[i]
        struct (muAcc, kAcc / sw)

    /// Evaluate all mixture properties at (T, P).
    /// Warnings from every pure-component evaluation are accumulated and propagated.
    let evaluate (mixture: Mixture) (t: float<K>) (p: float<bar>) : Thermo<MixtureProperties> =
        mixture.Components
        |> traverseList (fun (sp, y) ->
            PureComponent.viscosity sp t
            >>= fun mu ->
                PureComponent.conductivity sp t
                >>= fun k ->
                    PureComponent.specificHeatMass sp t
                    >>= fun cp -> ok (sp, y, float sp.MolarMass, float mu, float k, float cp))
        >>= fun items ->
            // Flat parallel arrays rather than an array of tuples: the tuple
            // form allocates a boxed tuple per species per access, and the
            // destructuring in the inner loop made that n^2 allocations.
            let n = List.length items
            let y = Array.zeroCreate n
            let m = Array.zeroCreate n
            let mu = Array.zeroCreate n
            let k = Array.zeroCreate n
            let cp = Array.zeroCreate n
            items |> List.iteri (fun i (_, yi, mi, mui, ki, cpi) ->
                y.[i] <- yi; m.[i] <- mi; mu.[i] <- mui; k.[i] <- ki; cp.[i] <- cpi)

            let mutable mMix = 0.0
            for i in 0 .. n - 1 do mMix <- mMix + y.[i] * m.[i]

            // Ideal gas. Real-gas Z belongs in a separate EoS module.
            let rhoMix = (float (barToPa p) * mMix / 1000.0) / (float Ru * float t)

            let mutable cpMix = 0.0
            for i in 0 .. n - 1 do cpMix <- cpMix + (y.[i] * m.[i] / mMix) * cp.[i]

            // One pass over the interaction matrix, accumulating both
            // denominators at once. Previously the matrix was built, then
            // traversed twice - once per property - with a fresh array of
            // partial sums allocated on every row of every traversal.
            let mutable muMix = 0.0
            let mutable kMix = 0.0
            let denominators = Array.zeroCreate n
            for i in 0 .. n - 1 do
                let mutable denom = 0.0
                for j in 0 .. n - 1 do
                    denom <- denom + y.[j] * phi mu.[i] mu.[j] m.[i] m.[j]
                denominators.[i] <- denom

            let mutable degenerate = false
            for i in 0 .. n - 1 do
                if denominators.[i] <= 0.0 || Double.IsNaN denominators.[i] then
                    degenerate <- true
                else
                    muMix <- muMix + y.[i] * mu.[i] / denominators.[i]
                    kMix <- kMix + y.[i] * k.[i] / denominators.[i]

            if degenerate then
                fail (InvalidMixture
                        "a Wilke interaction denominator is non-positive; a component has a \
                         non-physical viscosity or molar mass")
            elif kMix <= 0.0 then
                fail (InvalidMixture "the mixture conductivity is non-positive")
            else

            ok { MolarMass = mMix * 1.0<kg/kmol>
                 Density = rhoMix * 1.0<kg/m^3>
                 Viscosity = muMix * 1.0<Pa*s>
                 ThermalConductivity = kMix * 1.0<W/(m*K)>
                 SpecificHeat = cpMix * 1.0<J/(kg*K)>
                 Prandtl = cpMix * muMix / kMix }

    /// Mixture combination rule, exposed so it can be tested against an
    /// independent implementation on ITS OWN inputs. Testing only the end-to-end
    /// mixture result would conflate a bad mixing rule with bad species data.
    ///
    /// `components` are (mole fraction, molar mass, pure-species property).
    let combine (components: (float * float * float)[]) =
        let n = components.Length
        let phiMatrix =
            Array2D.init n n (fun i j ->
                let (_, mI, pI) = components.[i]
                let (_, mJ, pJ) = components.[j]
                phi pI pJ mI mJ)
        Array.init n (fun i ->
            let (yI, _, valueI) = components.[i]
            let denom =
                Array.init n (fun j ->
                    let (yJ, _, _) = components.[j]
                    yJ * phiMatrix.[i, j])
                |> Array.sum
            yI * valueI / denom)
        |> Array.sum

    /// Wassiljewa conductivity combination on its own inputs, with the interaction
    /// parameter built from the VISCOSITIES exactly as `evaluate` does. Passing
    /// conductivities to `combine` instead would build phi from the conductivity
    /// ratio, which is not the Wassiljewa rule.
    ///
    /// `components` are (mole fraction, molar mass, viscosity, conductivity).
    let combineConductivity (components: (float * float * float * float)[]) =
        let n = components.Length
        Array.init n (fun i ->
            let (yI, mI, muI, kI) = components.[i]
            let mutable denom = 0.0
            for j in 0 .. n - 1 do
                let (yJ, mJ, muJ, _) = components.[j]
                denom <- denom + yJ * phi muI muJ mI mJ
            yI * kI / denom)
        |> Array.sum

    /// Rigorous bounds any physically admissible mixture conductivity must obey:
    ///   1 / SUM(x_i / k_i)  <=  k_mix  <=  SUM(x_i k_i)
    let bounds (fractions: float[]) (values: float[]) =
        let upper = Array.map2 (*) fractions values |> Array.sum
        // The reciprocal bound divides by each component value, so a
        // non-positive conductivity would produce an infinity that looks like a
        // legitimate bound. Guarded rather than assumed.
        let reciprocal =
            Array.map2 (fun y v -> if v > 0.0 then y / v else nan) fractions values
            |> Array.sum
        let lower = if Double.IsNaN reciprocal || reciprocal <= 0.0 then 0.0 else 1.0 / reciprocal
        lower, upper

    /// Convenience: build a mixture from (key, mole amount) pairs against a loaded database.
    let fromKeys (db: Map<string, SpeciesData>) (composition: (string * float) list) : Thermo<Mixture> =
        composition
        |> traverseList (fun (key, y) ->
            WhbThermo.Data.SpeciesDatabase.find db key >>= fun sp -> ok (sp, y))
        >>= fun components ->
            match Mixture.create components with
            | Ok m -> ok m
            | Error e -> fail (InvalidMixture e)
