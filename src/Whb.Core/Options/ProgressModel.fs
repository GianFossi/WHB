namespace Whb.Core

module ExecutionProgress =

    type ProgressUpdate =
        { Description: string
          Fraction: float option }

    module Reporting =
        let private clamp01 value =
            max 0.0 (min 1.0 value)

        let description text =
            { Description = text
              Fraction = None }

        let step fraction text =
            { Description = text
              Fraction = Some (clamp01 fraction) }

        let scale startFraction endFraction (reportProgress: ProgressUpdate -> unit) (update: ProgressUpdate) =
            let startValue = clamp01 startFraction
            let endValue = clamp01 endFraction
            let scaledFraction =
                update.Fraction
                |> Option.map (fun fraction ->
                    startValue + (endValue - startValue) * clamp01 fraction)
            reportProgress
                { Description = update.Description
                  Fraction = scaledFraction }
