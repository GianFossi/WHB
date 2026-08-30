namespace Whb.Cli

open System
open System.IO

module OutputPaths =

    let private pathSeparators = [| Path.DirectorySeparatorChar; Path.AltDirectorySeparatorChar |]

    let private splitSegments (path: string) =
        path.Split(pathSeparators, StringSplitOptions.RemoveEmptyEntries)
        |> Array.toList

    let private cleanSegment (segment: string) =
        segment.Trim().TrimEnd(':')

    let private sanitizeSegments (path: string) =
        let sanitized =
            splitSegments path
            |> List.map cleanSegment
            |> List.filter (fun segment -> not (String.IsNullOrWhiteSpace segment) && segment <> "." && segment <> "..")
        if Path.IsPathRooted path then
            let rootedPrefixLength =
                Path.GetPathRoot(path)
                |> splitSegments
                |> List.map cleanSegment
                |> List.filter (fun segment -> not (String.IsNullOrWhiteSpace segment))
                |> List.length
            sanitized |> List.skip rootedPrefixLength
        else
            sanitized

    let private combineSegments (root: string) (segments: string list) =
        (root, segments)
        ||> List.fold (fun acc segment -> Path.Combine(acc, segment))

    let private fullPath (path: string) : string = Path.GetFullPath path

    let private ensureRooted (root: string) : string =
        if String.IsNullOrWhiteSpace root then fullPath "results"
        else fullPath root

    let private isUnderRoot (root: string) (candidate: string) =
        let rootFull = ensureRooted root
        let candidateFull = fullPath candidate
        let rootWithSeparator =
            if rootFull.EndsWith(string Path.DirectorySeparatorChar, StringComparison.Ordinal) then rootFull
            else rootFull + string Path.DirectorySeparatorChar
        candidateFull.Equals(rootFull, StringComparison.OrdinalIgnoreCase)
        || candidateFull.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)

    let private normalizePath (root: string) (requested: string) (fallbackName: string) =
        let reportRoot = ensureRooted root
        if String.IsNullOrWhiteSpace requested then reportRoot
        else
            let candidate = fullPath requested
            if isUnderRoot reportRoot candidate then candidate
            else
                let segments =
                    if Path.IsPathRooted requested then
                        sanitizeSegments requested |> List.tryLast |> Option.toList
                    else
                        sanitizeSegments requested
                match segments with
                | [] -> Path.Combine(reportRoot, fallbackName)
                | _ -> combineSegments reportRoot segments

    let reportDirectory root requested =
        normalizePath root requested "run"

    let reportFile root requested fallbackName =
        normalizePath root requested fallbackName
