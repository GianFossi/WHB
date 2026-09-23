namespace WhbThermo.Radiation

open System
open System.IO
open System.Reflection
open System.Text.Json
open System.Text.Json.Serialization
open Ganfoss.ROP
open WhbThermo.Domain
open WhbThermo.Data

/// Loads the radiation coefficient matrices and assembles the total internal
/// film coefficient (convection + gas radiation).
module RadiationModel =

    [<CLIMutable>]
    type private LecknerDto =
        { c: float[][]; tMinK: float; tMaxK: float
          pathMinBarM: float; pathMaxBarM: float; source: string }

    [<CLIMutable>]
    type private OverlapDto = { c: float[][]; cAsym: float[][]; source: string }

    [<CLIMutable>]
    type private RootDto =
        { schemaVersion: string; status: string
          h2o: LecknerDto; co2: LecknerDto; overlap: OverlapDto }

    let private options =
        let o = JsonSerializerOptions(PropertyNameCaseInsensitive = true)
        o.Converters.Add(JsonFsharpConverter())
        o

    let private toLeckner (d: LecknerDto) : Emissivity.LecknerCoefficients =
        { C = (if isNull d.c then [||] else d.c)
          TMin = d.tMinK * 1.0<K>
          TMax = d.tMaxK * 1.0<K>
          PathMin = d.pathMinBarM
          PathMax = d.pathMaxBarM
          Source = d.source }

    let parse (json: string) : Thermo<Emissivity.RadiationModel> =
        try
            let r = JsonSerializer.Deserialize<RootDto>(json, options)
            let model : Emissivity.RadiationModel =
                { Water = toLeckner r.h2o
                  Dioxide = toLeckner r.co2
                  Overlap = { C = (if isNull r.overlap.c then [||] else r.overlap.c)
                              CAsym = (if isNull r.overlap.cAsym then [||] else r.overlap.cAsym)
                              Source = r.overlap.source } }
            ok model
            |> warnIf (not model.Water.IsPopulated)
                      (RadiationCoefficientsPending ("Leckner", "H2O"))
            |> warnIf (not model.Dioxide.IsPopulated)
                      (RadiationCoefficientsPending ("Leckner", "CO2"))
            |> warnIf (not model.Overlap.IsPopulated)
                      (RadiationCoefficientsPending ("Leckner overlap", "H2O+CO2"))
        with ex ->
            fail (DatabaseParseError ex.Message)

    [<Literal>]
    let DefaultFile = "radiation-models.json"

    let load () : Thermo<Emissivity.RadiationModel> = DataStore.load DefaultFile parse

    let loadFile (path: string) : Thermo<Emissivity.RadiationModel> =
        DataStore.load path parse

    /// True when the model can actually be evaluated. Use as a design-time gate:
    /// a rating run that needs radiation should refuse to start otherwise.
    let isUsable (model: Emissivity.RadiationModel) =
        model.Water.IsPopulated && model.Dioxide.IsPopulated


/// Which correlation supplies the total gas emissivity.
type GasRadiationModel =
    /// Smith, Shen & Friedman (1982) WSGG. Coefficients are populated: this is
    /// the default and works out of the box.
    | SmithWsgg of Wsgg.Model
    /// Leckner (1972) chart correlation. Coefficient matrices ship EMPTY;
    /// evaluation fails until they are transcribed.
    | Leckner of Emissivity.RadiationModel

module GasRadiation =

    /// Uniform emissivity interface over both correlations.
    let emissivity (model: GasRadiationModel) (t: float<K>)
                   (pH2O: float<bar>) (pCO2: float<bar>) (le: float<m>) : Thermo<float> =
        match model with
        | SmithWsgg m -> Wsgg.emissivity m t pH2O pCO2 le
        | Leckner m -> Emissivity.gasEmissivity m t pH2O pCO2 le

    /// Load the default model. WSGG is chosen because it is the one that is
    /// actually usable without further data entry.
    let loadDefault () : Thermo<GasRadiationModel> =
        Wsgg.load () >>= fun m -> ok (SmithWsgg m)


/// Combines in-tube forced convection with participating-gas radiation.
module InternalFilm =

    /// Partial pressure of a component from its mole fraction and total pressure.
    let partialPressure (moleFraction: float) (total: float<bar>) : float<bar> =
        moleFraction * total

    /// Total internal film coefficient (manual s4):
    ///   h_int,tot = h_conv + h_rad
    ///
    /// Returns Failure if radiation is requested but its coefficients are not
    /// populated. Pass `includeRadiation = false` for a convection-only rating,
    /// which then carries an explicit warning that the result is non-conservative
    /// for a high-temperature WHB.
    let total (model: GasRadiationModel)
              (hConv: float<W/(m^2*K)>)
              (tGas: float<K>) (tWall: float<K>)
              (yH2O: float) (yCO2: float) (pressure: float<bar>)
              (geometry: Emissivity.Geometry)
              (tubeEmissivity: float)
              (includeRadiation: bool) : Thermo<float<W/(m^2*K)>> =

        if not includeRadiation then
            ok hConv
            |> warn (CorrelationExtrapolated
                        ("radiation",
                         "gas radiation excluded - non-conservative above ~700 degC"))
        else
            let le = Emissivity.meanBeamLength geometry
            let pW = partialPressure yH2O pressure
            let pC = partialPressure yCO2 pressure

            GasRadiation.emissivity model tGas pW pC le
            >>= fun epsGas ->
                let epsEff = Emissivity.effectiveEmissivity epsGas tubeEmissivity
                let hRad = Emissivity.radiativeCoefficient epsEff tGas tWall
                ok (hConv + hRad)
