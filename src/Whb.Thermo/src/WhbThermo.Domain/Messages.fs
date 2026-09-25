namespace WhbThermo.Domain

open Ganfoss.ROP

/// Structured message channel. Failures stop the pipeline; warnings ride along
/// with a valid result and must surface in the final calculation report.
[<AutoOpen>]
module Messages =

    type ThermoMessage =
        // --- failures ---
        | DatabaseNotFound      of path: string
        | DatabaseParseError    of detail: string
        | UnknownSpecies        of key: string
        | InvalidMixture        of detail: string
        | NoCpDataAvailable     of species: string
        // --- warnings ---
        | CpIsAnchorOnly        of species: string
        | OutsideFitRange       of species: string * property: string * tK: float * tMin: float * tMax: float
        | CorrelationExtrapolated of correlation: string * detail: string
        | MissingCriticalProps  of species: string
        | RadiationCoefficientsPending of model: string * species: string
        | NoRadiatingSpecies

        override this.ToString() =
            match this with
            | DatabaseNotFound p -> $"Species database not found: %s{p}"
            | DatabaseParseError d -> $"Species database parse error: %s{d}"
            | UnknownSpecies k -> $"Unknown species key '%s{k}'"
            | InvalidMixture d -> $"Invalid mixture: %s{d}"
            | NoCpDataAvailable s -> $"No Cp data for '%s{s}'"
            | CpIsAnchorOnly s ->
                $"Cp for '%s{s}' is a single 500 degC anchor with no temperature dependence \
                  - feasibility grade only, not valid for a signed calculation report"
            | OutsideFitRange (s, p, t, lo, hi) ->
                $"%s{s}: %s{p} evaluated at %.1f{t} K, outside fit range %.0f{lo}-%.0f{hi} K (extrapolated)"
            | CorrelationExtrapolated (c, d) -> $"%s{c} extrapolated: %s{d}"
            | MissingCriticalProps s -> $"%s{s}: no critical properties, ideal gas Z=1 assumed"
            | RadiationCoefficientsPending (m, s) ->
                $"%s{m} coefficients for %s{s} are not populated - gas radiation cannot be \
                  evaluated. Fill radiation-models.json from Leckner (1972) or VDI Waermeatlas."
            | NoRadiatingSpecies ->
                "Mixture contains no radiating species (H2O/CO2) - gas radiation set to zero"

    /// Project-wide ROP alias.
    type Thermo<'T> = Returns<'T, ThermoMessage>
