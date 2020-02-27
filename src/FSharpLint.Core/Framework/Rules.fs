﻿module FSharpLint.Framework.Rules

open FSharp.Compiler.Ast
open FSharp.Compiler.Range
open FSharp.Compiler.SourceCodeServices
open FSharpLint.Framework.AbstractSyntaxArray
open FSharpLint.Framework.Ast
open FSharpLint.Framework.Suggestion

type AstNodeRuleParams =
    { astNode : AstNode
      nodeHashcode : int
      nodeIndex : int
      syntaxArray : AbstractSyntaxArray.Node []
      skipArray : Skip []
      getParents : int -> AstNode list
      filePath : string
      fileContent : string
      checkInfo : FSharpCheckFileResults option }

type LineRuleParams =
    { line : string
      lineNumber : int
      isLastLine : bool
      filePath : string
      fileContent : string }

type RuleMetadata<'config> =
  { name : string
    identifier : string
    ruleConfig : 'config }

type AstNodeRuleConfig =
  { runner : AstNodeRuleParams -> WarningDetails []
    cleanup : unit -> unit }

type NamingCase =
    | PascalCase = 0
    | CamelCase = 1

type NamingUnderscores =
    | None = 0
    | AllowPrefix = 1
    | AllowAny = 2

type NamingConfig =
    { naming : NamingCase option
      underscores : NamingUnderscores option
      prefix : string option
      suffix : string option }

type NamingRuleConfig =
    { config : NamingConfig
      getIdentifiersToCheck : AstNodeRuleParams -> (Ident * string * Async<bool> option) [] }

type LineRuleConfig = { runner : LineRuleParams -> WarningDetails [] }

type LineRuleConfigWithContext<'Context> = { runner : 'Context -> LineRuleParams -> WarningDetails [] }

type IndentationRuleConfig = LineRuleConfigWithContext<Map<int,bool*int>>
type NoTabCharactersRuleConfig = LineRuleConfigWithContext<(string*range) list>

type Rule =
  | AstNodeRule of RuleMetadata<AstNodeRuleConfig>
  | LineRule of RuleMetadata<LineRuleConfig>
  | IndentationRule of RuleMetadata<IndentationRuleConfig>
  | NoTabCharactersRule of RuleMetadata<NoTabCharactersRuleConfig>

let toWarning (identifier:string) (ruleName:string) (filePath:string) (fileContents:string) (details:WarningDetails) =
    {
        LintWarning.RuleIdentifier = identifier
        FilePath = filePath
        RuleName = ruleName
        ErrorText = fileContents.Split('\n').[details.Range.StartLine - 1].TrimEnd('\r')
        Details = details
    }

let runAstNodeRule (rule:RuleMetadata<AstNodeRuleConfig>) (config:AstNodeRuleParams) =
    rule.ruleConfig.runner config
    |> Array.map (toWarning rule.identifier rule.name config.filePath config.fileContent)

let runLineRuleWithContext (rule:RuleMetadata<LineRuleConfigWithContext<'Context>>) (context:'Context) (config:LineRuleParams) =
    rule.ruleConfig.runner context config
    |> Array.map (toWarning rule.identifier rule.name config.filePath config.fileContent)

let runLineRule (rule:RuleMetadata<LineRuleConfig>) (config:LineRuleParams) =
    rule.ruleConfig.runner config
    |> Array.map (toWarning rule.identifier rule.name config.filePath config.fileContent)
