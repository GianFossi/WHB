namespace WhbThermo.Domain

/// Where a number came from, carried alongside the number itself.
///
/// The library already has two ways to signal a data problem:
///
///   * ROP Failure    - no data at all, nothing can be computed
///   * ROP warning    - computed, but the result needs a caveat
///
/// Those cover the moment of calculation. What they do not cover is the moment
/// of REPORTING, an hour and forty function calls later, when the warnings have
/// been aggregated, deduplicated or simply not read, and a number that was
/// estimated looks exactly like a number that was measured.
///
/// Provenance is the third mechanism: a tag that travels WITH the value through
/// every arithmetic step, so a calculation report can state the weakest input
/// behind any result without reconstructing the call graph. A mixture property
/// computed from one measured and one estimated component is Estimated - the
/// combination takes the worst of its parts, which is the honest rule and also
/// the only one that cannot be gamed.
[<AutoOpen>]
module Provenance =

    type DataQuality =
        /// Direct experimental data, or a correlation fitted to it and
        /// validated against an independent source.
        | Measured
        /// A published fit, used inside its stated validity range.
        | Fitted
        /// Estimated by a predictive method - Chung, Joback, group
        /// contribution. Usable, not quotable.
        | Estimated
        /// Extrapolated beyond the validity range of its own correlation.
        | Extrapolated
        /// No data. Reaching this in a result means something upstream should
        /// have failed and did not.
        | Unavailable

        /// Ordering from best to worst, for taking the weakest of a set.
        member this.Rank =
            match this with
            | Measured -> 0
            | Fitted -> 1
            | Estimated -> 2
            | Extrapolated -> 3
            | Unavailable -> 4

        member this.IsQuotable =
            match this with
            | Measured | Fitted -> true
            | _ -> false

    /// A value with its provenance and a short note naming the source.
    type Qualified<'T> =
        { Value   : 'T
          Quality : DataQuality
          Source  : string }

        member this.IsQuotable = this.Quality.IsQuotable

    module Qualified =

        let create quality source value =
            { Value = value; Quality = quality; Source = source }

        let measured source value = create Measured source value
        let fitted source value = create Fitted source value
        let estimated source value = create Estimated source value

        let map f (q: Qualified<'T>) =
            { Value = f q.Value; Quality = q.Quality; Source = q.Source }

        /// Combine two qualified values. The result takes the WORSE quality and
        /// names the source that produced it: a duty computed from one measured
        /// and one estimated property is an estimate, not a measurement.
        let combine f (a: Qualified<'T>) (b: Qualified<'U>) : Qualified<'V> =
            let worseQuality, worseSource =
                if a.Quality.Rank >= b.Quality.Rank then a.Quality, a.Source
                else b.Quality, b.Source
            { Value = f a.Value b.Value
              Quality = worseQuality
              Source = worseSource }

        /// Weakest quality across a set, with the source responsible for it.
        let weakest (items: Qualified<'T> list) =
            match items with
            | [] -> Unavailable, "no inputs"
            | _ ->
                let worst = items |> List.maxBy (fun q -> q.Quality.Rank)
                worst.Quality, worst.Source

        /// Every input that is not quotable, for a report appendix.
        let caveats (items: Qualified<'T> list) =
            items
            |> List.filter (fun q -> not q.IsQuotable)
            |> List.map (fun q -> q.Quality, q.Source)
            |> List.distinct
