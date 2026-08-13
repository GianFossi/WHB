namespace Whb.Core

open System
open System.Diagnostics
open System.IO
open Whb.Core.Options

/// <summary>
/// Provides githubtransfer functionality for the WHB calculation model.
/// </summary>
/// <remarks>
/// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
/// </remarks>
module GitHubTransfer =

    [<CLIMutable>]
    /// <summary>
    /// Represents transferplan data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type TransferPlan =
        { RepositoryUrl: string
          Branch: string
          CommitMessage: string
          CreatePullRequest: bool
          Commands: string list }

    /// <summary>
    /// Calculates or returns plan for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let plan (options: Options.ProjectOptions) =
        let g = options.Github
        let branch = if String.IsNullOrWhiteSpace g.Branch then "main" else g.Branch
        let message = if String.IsNullOrWhiteSpace g.CommitMessage then "Update WHB project" else g.CommitMessage
        let escapedMessage = message.Replace("\"", "\\\"")
        { RepositoryUrl = g.RepositoryUrl
          Branch = branch
          CommitMessage = message
          CreatePullRequest = g.CreatePullRequest
          Commands =
            [ "git init"
              if not (String.IsNullOrWhiteSpace g.RepositoryUrl) then $"git remote add origin {g.RepositoryUrl}"
              $"git checkout -B {branch}"
              "git add ."
              $"git commit -m \"{escapedMessage}\""
              $"git push -u origin {branch}"
              if g.CreatePullRequest then "gh pr create --draft --fill" ] }

    let private runCommand (workingDir: string) (fileName: string) (args: string) =
        let psi = ProcessStartInfo(fileName, args)
        psi.WorkingDirectory <- workingDir
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi.UseShellExecute <- false
        use p = Process.Start(psi)
        p.WaitForExit()
        let output = p.StandardOutput.ReadToEnd()
        let err = p.StandardError.ReadToEnd()
        if p.ExitCode = 0 then Ok output else Error err

    /// <summary>
    /// Calculates or returns execute for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let execute workingDir (options: Options.ProjectOptions) =
        let g = options.Github
        if String.IsNullOrWhiteSpace g.RepositoryUrl then
            Error "RepositoryUrl GitHub mancante nelle opzioni."
        else
            let branch = if String.IsNullOrWhiteSpace g.Branch then "main" else g.Branch
            let message = if String.IsNullOrWhiteSpace g.CommitMessage then "Update WHB project" else g.CommitMessage
            let escapedMessage = message.Replace("\"", "\\\"")
            let gitDir = Path.Combine(workingDir, ".git")
            let init =
                if Directory.Exists gitDir then Ok "" else runCommand workingDir "git" "init"
            init
            |> Result.bind (fun _ ->
                match runCommand workingDir "git" "remote get-url origin" with
                | Ok _ -> Ok ""
                | Error _ -> runCommand workingDir "git" $"remote add origin {g.RepositoryUrl}")
            |> Result.bind (fun _ -> runCommand workingDir "git" $"checkout -B {branch}")
            |> Result.bind (fun _ -> runCommand workingDir "git" "add .")
            |> Result.bind (fun _ -> runCommand workingDir "git" $"commit -m \"{escapedMessage}\"")
            |> Result.bind (fun _ -> runCommand workingDir "git" $"push -u origin {branch}")
