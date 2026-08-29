namespace Whb.Core

open System
open System.Threading

/// <summary>
/// Shared runtime settings for WHB calculation orchestration layers.
/// </summary>
/// <remarks>
/// These settings influence numerical strategy and parallel execution without changing the
/// physical input case or the engineering formulas used by the verification engine.
/// </remarks>
module DesignRuntime =

    type ProgressUpdate = ExecutionProgress.ProgressUpdate

    type RunSettings =
        { BypassMapMode: string
          BypassTargetToleranceK: float
          GasPropertyCache: bool
          CorrelationValidityWarnings: bool
          /// Maximum number of bypass-map points evaluated concurrently. Each point is an
          /// independent solve of the same immutable case, so this changes run time only,
          /// never results. Use 1 to force a strictly sequential run.
          Parallelism: int }

    /// <summary>
    /// Conservative default runtime settings for callers that do not pass project options.
    /// </summary>
    let defaultRunSettings =
        { BypassMapMode = "adaptive"
          BypassTargetToleranceK = 0.5
          GasPropertyCache = true
          CorrelationValidityWarnings = true
          Parallelism = max 1 Environment.ProcessorCount }

    /// <summary>
    /// Process-wide budget of concurrent solves.
    /// </summary>
    /// <remarks>
    /// `Parallelism` is a per-design setting, so nesting a parallel caller above a design run
    /// would multiply rather than share the machine. The budget is a single gate for the whole
    /// process: whoever wants a worker takes a slot from it, so total concurrency stays bounded
    /// no matter how many levels start running at once.
    /// </remarks>
    module ParallelBudget =
        let private gate =
            new SemaphoreSlim(max 1 Environment.ProcessorCount, max 1 Environment.ProcessorCount)

        let acquire () = gate.Wait()
        let release () = gate.Release() |> ignore
        let available () = gate.CurrentCount
