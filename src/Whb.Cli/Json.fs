namespace Whb.Cli

open System
open System.IO
open System.Text.Json
open System.Globalization
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open Whb.Core
open Whb.Core.Constants
open Whb.Core.Types
open Whb.Core.Options

module Json =
    /// Try to get a property from a JSON object using a dot-separated path.
    /// If the property is found, return Some(JsonElement), otherwise return None.
    /// The path is a string of property names separated by dots, e.g. "gas.composizione".
    /// Example: tryPath root "gas.composizione"
    /// If the path is invalid or the property is not found, return None.
    let private tryPath (root: JsonElement) (path: string) =
        let parts = path.Split('.')
        let mutable cur = root
        let mutable ok = true
        for p in parts do
            if ok then
                if cur.ValueKind = JsonValueKind.Object then
                    match cur.TryGetProperty p with
                    | true, v -> cur <- v
                    | _ -> ok <- false
                else ok <- false
        if ok then Some cur else None
    let f (root: JsonElement) (path: string) (def: float) =
        match tryPath root path with
        | Some v when v.ValueKind = JsonValueKind.Number -> v.GetDouble()
        | _ -> def
    let i (root: JsonElement) (path: string) (def: int) =
        match tryPath root path with
        | Some v when v.ValueKind = JsonValueKind.Number -> v.GetInt32()
        | _ -> def
    let b (root: JsonElement) (path: string) (def: bool) =
        match tryPath root path with
        | Some v when v.ValueKind = JsonValueKind.True -> true
        | Some v when v.ValueKind = JsonValueKind.False -> false
        | _ -> def
    let s (root: JsonElement) (path: string) (def: string) =
        match tryPath root path with
        | Some v when v.ValueKind = JsonValueKind.String -> v.GetString()
        | _ -> def
    let compositionAt (root: JsonElement) (path: string) (def: GasProps.Composition) =
        match tryPath root path with
        | Some v when v.ValueKind = JsonValueKind.Object ->
            let res =
                v.EnumerateObject()
                |> Seq.choose (fun p ->
                    match GasProps.tryParseSpecies p.Name with
                    | Some sp -> Some(sp, p.Value.GetDouble())
                    | None ->
                        eprintfn "ATTENZIONE: specie '%s' non riconosciuta nella composizione: ignorata." p.Name
                        None)
                |> List.ofSeq
            if res.IsEmpty then def else res
        | _ -> def
    let composition (root: JsonElement) (def: GasProps.Composition) =
        compositionAt root "gas.composizione" def
    let tryArray (root: JsonElement) (path: string) =
        match tryPath root path with
        | Some v when v.ValueKind = JsonValueKind.Array ->
            Some(v.EnumerateArray() |> Seq.map (fun x -> x.GetDouble()) |> List.ofSeq)
        | _ -> None
    let lengths (root: JsonElement) (path: string) =
        match tryPath root path with
        | Some v when v.ValueKind = JsonValueKind.Array ->
            let res =
                v.EnumerateArray()
                |> Seq.map (fun e ->
                    let g name d =
                        match e.TryGetProperty(name: string) with
                        | true, x when x.ValueKind = JsonValueKind.Number -> x.GetDouble()
                        | _ -> d
                    (g "frazione" 1.0, g "lunghezza_mm" 200.0 / 1000.0))
                |> List.ofSeq
            if res.IsEmpty then None else Some res
        | _ -> None
    let lines (root: JsonElement) (path: string) (def: Piping.Line list) =
        match tryPath root path with
        | Some v when v.ValueKind = JsonValueKind.Array ->
            let res =
                v.EnumerateArray()
                |> Seq.map (fun e ->
                    let gd name d =
                        match e.TryGetProperty(name: string) with
                        | true, x when x.ValueKind = JsonValueKind.Number -> x.GetDouble()
                        | _ -> d
                    let gs name d =
                        match e.TryGetProperty(name: string) with
                        | true, x when x.ValueKind = JsonValueKind.String -> x.GetString()
                        | _ -> d
                    let straights =
                        match e.TryGetProperty "diritti_mm" with
                        | true, a when a.ValueKind = JsonValueKind.Array ->
                            a.EnumerateArray() |> Seq.map (fun x -> x.GetDouble() / 1000.0) |> List.ofSeq
                        | _ -> []
                    let elbows =
                        match e.TryGetProperty "curve" with
                        | true, a when a.ValueKind = JsonValueKind.Array ->
                            a.EnumerateArray()
                            |> Seq.map (fun c ->
                                let g2 n d =
                                    match c.TryGetProperty(n: string) with
                                    | true, x when x.ValueKind = JsonValueKind.Number -> x.GetDouble()
                                    | _ -> d
                                ({ AngleDeg = g2 "gradi" 90.0
                                   ROverD = g2 "r_su_d" 1.5
                                   Count = int (g2 "n" 1.0) } : Piping.Elbow))
                            |> List.ofSeq
                        | _ -> []
                    ({ Tag = gs "tag" "?"
                       Nps = gs "nps" "?"
                       Id = gd "id_mm" 400.0 / 1000.0
                       Count = int (gd "n" 1.0)
                       Straights = straights
                       Elbows = elbows
                       ExtraK = gd "k_extra" 0.0
                       ZNozzle = gd "z_m" 0.0
                       AngleDeg = gd "angolo_gradi" 0.0
                       Connected =
                         (match e.TryGetProperty "collegato" with
                          | true, x when x.ValueKind = JsonValueKind.False -> false
                          | _ -> true)
                       Note = gs "nota" "" } : Piping.Line))
                |> List.ofSeq
            if res.IsEmpty then def else res
        | _ -> def


