namespace Whb.Core

open ROP
open WhbThermo.Domain
open WhbThermo.Data
open WhbThermo.Properties

/// <summary>
/// Thin Whb.Core facade over the WhbThermo species database.
/// </summary>
/// <remarks>
/// Converts WhbThermo units and result types to the SI floats used across Whb.Core. Species data
/// (molar mass, NASA-9 heat capacity and enthalpy, NASA CEA / Sutherland transport, critical
/// constants) live in <c>src/Whb.Thermo/data/species-database.json</c> only; do not copy
/// coefficients back into Whb.Core. WhbThermo warnings (extrapolated fit range, missing critical
/// constants) are not propagated: this facade feeds the solver hot path and has no warning channel.
/// </remarks>
module GasThermoAdapter =

    /// <summary>
    /// Species data resolved once from the WhbThermo database, in Whb.Core units.
    /// </summary>
    type SpeciesRecord =
        { Key: string
          Data: SpeciesData
          /// Molar mass [kg/mol].
          MolarMass: float
          /// Standard formation enthalpy at 298.15 K [J/mol].
          FormationEnthalpy: float
          /// Tc [K], Pc [Pa], acentric factor, Vc [m³/mol]; None when any of them is missing.
          Critical: (float * float * float * float) option
          /// True when the database gives this species' second virial coefficient from
          /// IAPWS-IF97 instead of the corresponding-states correlation (water).
          If97SecondVirial: bool }

    let private valueOf (what: string) (result: Thermo<'T>) : 'T =
        match result with
        | Returns.Success (value, _) -> value
        | Returns.Failure errors ->
            failwithf "WhbThermo %s: %s" what (errors |> List.map string |> String.concat "; ")

    let private database = lazy (SpeciesDatabase.load () |> valueOf "species database")
    let private binaryTable = lazy (BinaryInteraction.load () |> valueOf "binary interaction table")
    let private waterGasShift =
        lazy (ReactionDatabase.load ()
              |> Returns.bind (fun reactions -> ReactionDatabase.find reactions "waterGasShift")
              |> valueOf "water-gas shift reaction")

    /// <summary>
    /// Resolves a species by its WhbThermo key and converts its constant data to Whb.Core units.
    /// </summary>
    /// <param name="key">The WhbThermo species key, for example <c>"CO2"</c> or <c>"Ar"</c>.</param>
    /// <returns>The resolved species record; throws when the key is unknown or its data is invalid.</returns>
    let record (key: string) : SpeciesRecord =
        let sp = SpeciesDatabase.find (database.Force()) key |> valueOf key
        let critical =
            sp.Critical
            |> Option.bind (fun c ->
                c.Vc
                |> Option.map (fun vc ->
                    (float c.Tc, float c.Pc * 1.0e5, c.Acentric, vc * 1.0e-6)))   // bar -> Pa, cm³ -> m³
        { Key = key
          Data = sp
          MolarMass = float sp.MolarMass / 1000.0
          FormationEnthalpy = SpeciesApi.formationEnthalpy sp |> valueOf $"{key} formation enthalpy"
          Critical = critical
          If97SecondVirial =
            sp.EosParametersFor EquationOfState.Virial
            |> Option.bind (fun p -> p.SecondVirial)
            |> Option.exists (fun source -> source.Contains "IF97") }

    /// <summary>
    /// Virial binary interaction parameter of a species pair, 0 when the table has none.
    /// </summary>
    /// <remarks>
    /// Temperature independent as used here: the virial pair terms are built once per species
    /// set, so a k_ij with a fitted temperature range is applied without its range check.
    /// </remarks>
    let virialKij (a: string) (b: string) =
        BinaryInteraction.tryFind (binaryTable.Force()) a b EquationOfState.Virial
        |> Option.map (fun p -> p.Kij)
        |> Option.defaultValue 0.0

    /// <summary>
    /// Natural logarithm of the water-gas shift equilibrium constant CO + H2O = CO2 + H2,
    /// from the NASA-9 Gibbs energies of the database (partial pressures in bar).
    /// </summary>
    let waterGasShiftLnK (tK: float) =
        let k =
            ReactionDatabase.equilibriumConstant (database.Force()) (waterGasShift.Force()) (tK * 1.0<K>)
            |> valueOf "water-gas shift constant"
        k.LogK

    /// <summary>Ideal-gas molar heat capacity [J/(mol·K)].</summary>
    let cpMolar (r: SpeciesRecord) (tK: float) =
        float (PureComponent.specificHeatMass r.Data (tK * 1.0<K>) |> valueOf $"{r.Key} cp")
        * r.MolarMass

    /// <summary>Absolute molar enthalpy including the formation term [J/mol].</summary>
    let hMolarAbs (r: SpeciesRecord) (tK: float) =
        Equilibrium.molarEnthalpy r.Data (tK * 1.0<K>) |> valueOf $"{r.Key} enthalpy"

    /// <summary>Low-pressure dynamic viscosity [Pa·s].</summary>
    let viscosity (r: SpeciesRecord) (tK: float) =
        float (PureComponent.viscosity r.Data (tK * 1.0<K>) |> valueOf $"{r.Key} viscosity")

    /// <summary>Low-pressure thermal conductivity [W/(m·K)].</summary>
    let conductivity (r: SpeciesRecord) (tK: float) =
        float (PureComponent.conductivity r.Data (tK * 1.0<K>) |> valueOf $"{r.Key} conductivity")
