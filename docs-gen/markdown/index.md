# FSharpLint

FSharpLint is a lint tool for F#. It can be run as a dotnet tool, and also integrates with Ionide for VS Code.

> The term [lint] is now applied generically to tools that flag suspicious usage in software written in any computer language - [_Wikipedia_](http://en.wikipedia.org/wiki/Lint_(software))

Using a `.fsproj` (F# project) or `.sln` (F# solution) file the tool will analyse all of the F# implementation files in the project/solution looking for code that breaks a set of rules governing the style of the code. Examples of rules: lambda functions must be less than 6 lines long, class member identifiers must be PascalCase.

#### Example Usage of the Tool

The following program:

    type ExampleInterface =
       abstract member print : unit -> unit

    [<EntryPoint>]
    let main argv =
        let x = List.fold (fun x y -> x + y) 0 [1;2;3]
        printfn "%d" x
        0

Run against the lint tool generates the following errors:

	[lang=error]


    FL0036: Consider changing `ExampleInterface` to be prefixed with `I`.
    Consider changing `ExampleInterface` to be prefixed with `I`.
    Error in file Program.fs on line 1 starting at column 5
    type ExampleInterface =
         ^

    FL0045: Consider changing `print` to PascalCase.
    Error in file Program.fs on line 2 starting at column 19
       abstract member print : unit -> unit
                       ^

    FL0034: If `( + )` has no mutable arguments partially applied then the lambda can be removed.
    Error in file Program.fs on line 6 starting at column 23
        let x = List.fold (fun x y -> x + y) 0 [1;2;3]
                           ^

Refactored using lint's warnings:

    type IExampleInterface =
       abstract member Print : unit -> unit

    [<EntryPoint>]
    let main argv =
        let x = List.fold (+) 0 [1;2;3]
        printfn "%d" x
        0

If we run lint again it will find a new error, it's worth running the tool until it no longer finds any errors:

	[lang=error]
    FL0065: `List.fold ( + ) 0 x` might be able to be refactored into `List.sum x`.
    Error in file Program.fs on line 6 starting at column 12
    let x = List.fold (+) 0 [1;2;3]
            ^

After refactoring again we have with no lint errors:

    type IExampleInterface =
       abstract member Print : unit -> unit

    [<EntryPoint>]
    let main argv =
        let x = List.sum [1;2;3]
        printfn "%d" x
        0

#### Building The Tool

On windows run `build.cmd` and on unix based systems run `build.sh`.

#### Running The Tool

FSharpLint can be used in several ways:

* [Running as dotnet tool from command line](ConsoleApplication.html).
* [In VS Code using the Ionide-FSharp plugin](https://marketplace.visualstudio.com/items?itemName=Ionide.Ionide-fsharp).
* [In other IDEs (Visual Studio, Rider) as an MSBuild Task](MSBuildTask.html).

#### Rules

See a full list of the available rules [here](Rules.html). Each rule has its own page with more information.

#### Suppressing rules in code

Rules can be disabled within the code using structured comments. See the [Suppressing Warnings](Suppression.html) page for more information.

#### Configuration Files

Configuration of the tool is done using JSON. Configuration files must be named: `fsharplint.json`. A single JSON file containing the default configuration for all rules is [included inside of the software](https://github.com/fsprojects/FSharpLint/blob/master/src/FSharpLint.Core/DefaultConfiguration.json).

The configuration files are loaded in a specific order, files loaded after another will override the previous file. The default configuration is loaded first, from there the tool checks each directory from the root to up to the project being linted's directory. For example if the path of the project being linted was `C:\Files\SomeProjectBeingLinted`, then `C:\` would first be checked for a config file - if a config file is found then it would override the default config, then `C:\Files` would be checked - if a config file was found and a config file was also previously found in `C:\` then the config in `C:\Files` would override the one in `C:\` and so on.

The configuration rules are overridden by redefining any properties of an rule that you want to override, for example if you wanted to turn on the type prefixing rule which has the default configuration of:

	[lang=javascript]
    {
      "formatting": {
          "typePrefixing": { "enabled": false }
      }
    }

To override to turn off you'd set enabled to true in your own configuration file as follows:

	[lang=javascript]
    {
      "formatting": {
          "typePrefixing": { "enabled": true }
      }
    }

Previously, configuration was specified in XML format. You can automatically convert your XML config to the JSON format using the dotnet tool:

    dotnet fsharplint -convert <path-to-xml> (output-path>

#### Ignoring Files

In the configuration file paths can be used to specify files that should be included, globs are used to match wildcard directories and files. For example the following will match all files with the file name assemblyinfo (the matching is case insensitive) with any extension:

  { "ignoreFiles": ["assemblyinfo.*"] }

* Directories in the path must be separated using `/`
* If the path ends with a `/` then everything inside of a matching directory shall be excluded.
* If the path does not end with a `/` then all matching files are excluded.

#### Running Lint From An Application

Install the [`FSharp.Core` nuget package](https://www.nuget.org/packages/FSharpLint.Core/).

The namespace `FSharpLint.Application` contains a module named `Lint` which provides several functions
to lint a project/source file/source string.
