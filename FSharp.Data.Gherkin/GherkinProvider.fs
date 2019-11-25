﻿namespace ProviderImplementation

open FSharp.Quotations
open FSharp.Core.CompilerServices
open System.Reflection
open ProviderImplementation.ProvidedTypes

open Gherkin

type Example (column:string,value:string) =
    member __.Column = column
    member __.Value = value

[<TypeProvider>]
type GherkinProvider (config : TypeProviderConfig) as this =
    inherit TypeProviderForNamespaces (config)

    let ns = "FSharp.Data.Gherkin"
    let parser = Parser()
    let asm = Assembly.GetExecutingAssembly()
    


    let createStep (gherkinStep:Ast.Step) (order:int) (stepName:string)=
        let stepText = gherkinStep.Text
        let stepKeyword = gherkinStep.Keyword
        let step = ProvidedTypeDefinition(stepName, Some typeof<obj>, isErased=false, hideObjectMethods=true, nonNullable=true)

        ProvidedProperty("StepText",typeof<string>,isStatic = false, getterCode = fun _ -> <@@ stepText @@> ) |> step.AddMember
        ProvidedProperty("StepKeyword",typeof<string>,isStatic = false, getterCode = fun _ -> <@@ stepKeyword @@> ) |> step.AddMember
        ProvidedProperty("Order",typeof<int>,isStatic = false, getterCode = fun _ -> <@@ order @@> ) |> step.AddMember

        step

    
    let createScenario (gherkinScenario:Ast.Scenario) =
        let scenarioName = gherkinScenario.Name
        let scenarioDesc = gherkinScenario.Description

        let scenario = ProvidedTypeDefinition(scenarioName, Some typeof<obj>, isErased=false, hideObjectMethods=true, nonNullable = true)

        gherkinScenario.Steps
        |> Seq.iteri(
            fun i s ->
                let stepName = sprintf "%i. %s%s" i s.Keyword s.Text
                let step = createStep s i stepName
                step |> scenario.AddMember 

                ProvidedProperty(stepName,step.AsType(),isStatic = false, getterCode=fun _ -> <@@ obj() @@>) |> scenario.AddMember)

        ProvidedProperty("ScenarioName",typeof<string>,isStatic = false, getterCode = fun _ -> <@@ scenarioName @@> ) |> scenario.AddMember
        ProvidedProperty("ScenarioDescription",typeof<string>,isStatic = false, getterCode = fun _ -> <@@ scenarioDesc @@> ) |> scenario.AddMember

        scenario

    
    let createBackground (gherkinBackground:Ast.Background) =
        let backgroundName = gherkinBackground.Name
        let backgroundDesc = gherkinBackground.Description

        let background = ProvidedTypeDefinition("Background", Some typeof<obj>, isErased=false, hideObjectMethods=true, nonNullable=true)
        
        gherkinBackground.Steps
        |> Seq.iteri(
            fun i s ->
                let stepName = sprintf "%i. %s%s" i s.Keyword s.Text
                let step = createStep s i s.Text
                step |> background.AddMember 

                ProvidedProperty(stepName,step.AsType(),isStatic = false, getterCode=fun _ -> <@@ obj() @@>) |> background.AddMember)
        
        ProvidedProperty("BackgroundName",typeof<string>,isStatic = false, getterCode = fun _ -> <@@ backgroundName @@> ) |> background.AddMember
        ProvidedProperty("BackgroundDescription",typeof<string>,isStatic = false, getterCode = fun _ -> <@@ backgroundDesc @@> ) |> background.AddMember
        
        background

    
    let createFeature providerName (path:string) =
        let document = parser.Parse(path)
        let providedAssembly = ProvidedAssembly()
        let featureName = document.Feature.Name
        let featureDesc = document.Feature.Description        

        let root = ProvidedTypeDefinition(providedAssembly, ns, providerName, Some typeof<obj>, hideObjectMethods=true, nonNullable=true, isErased=false)
        let featureType = ProvidedTypeDefinition(providedAssembly,ns,featureName, Some typeof<obj>, hideObjectMethods=true, nonNullable=true, isErased=false)

        let scenarios = ProvidedTypeDefinition(providedAssembly, ns, "Scenarios", Some typeof<obj>, isErased=false,hideObjectMethods=true, nonNullable=true)
        let scenarioOutlines = ProvidedTypeDefinition(providedAssembly, ns, "ScenarioOutlines", Some typeof<obj>, isErased=false, hideObjectMethods=true, nonNullable=true)

        let examplesConstructor = (typeof<Example>).GetConstructors().[0]

        let createExampleType (gherkinScenario:Ast.Scenario) =
            let examplesName = sprintf "%s example" gherkinScenario.Name
            let header = (gherkinScenario.Examples |> Seq.toList).[0].TableHeader.Cells |> Seq.map (fun c ->c.Value) |> Seq.toList
            let exampleType  = ProvidedTypeDefinition(examplesName,Some typeof<obj>, isErased=false, hideObjectMethods=true, nonNullable=true)

            let parameters = 
                header 
                |> List.map(fun h -> ProvidedParameter(h,typeof<string>))
            
            //change thiese types from string to whatever
            let fields = 
                header 
                |> List.map(fun h -> ProvidedField( sprintf "_%s" h,typeof<Example>))

            fields |> Seq.iter (exampleType.AddMember)

            let properties =
                header
                |> Seq.mapi(fun i h -> ProvidedProperty(h,typeof<Example>,getterCode=fun args -> Expr.FieldGet(args.[0],fields.[i])))

            properties |> Seq.iter (exampleType.AddMember)

            let createExample (column:string) (value:Expr) = Expr.NewObject(examplesConstructor,[Expr.Value(column);value])

            let ctr =
                ProvidedConstructor(parameters,
                    fun args -> 
                        match args with
                        | this :: xs -> 
                            match xs with
                            | e :: ex when ex.Length = 0 -> Expr.FieldSet(this,fields.[0],e)
                            | e :: ex when ex.Length <> 0 -> 
                                let first =  Expr.FieldSet(this,fields.[0],(createExample header.[0] e))
                                let rest = ex |> List.mapi(fun i expr -> Expr.FieldSet(this,fields.[i+1],(createExample header.[i+1] expr)))
                                rest |> List.fold (fun a c -> Expr.Sequential(a,c)) first
                                
                            | _ -> failwith ("incorrect constructor arguments")
                        | _ -> failwith ("incorrect constructor arguments"))
            
            ctr |> exampleType.AddMember
            exampleType

        let createExampleInstances (scenarioOutline:Ast.Scenario) (exampleType:ProvidedTypeDefinition) = 
            let ctr = exampleType.GetConstructors().[0]

            let  examples =
                scenarioOutline.Examples
                |> Seq.collect(fun e -> e.TableBody)
                |> Seq.map (fun r -> 
                        r.Cells
                        |> Seq.map (fun c -> Expr.Value(c.Value,typeof<string>)) |> Seq.toList)
                |> Seq.map (fun args -> Expr.NewObject(ctr,args) )
                |> Seq.toList
            
            Expr.NewArray(exampleType,examples)

        
        document.Feature.Children
        |> Seq.iter(
            fun c ->
                match c with
                | :? Ast.Background ->
                        let gherkinBackground = c :?> Ast.Background
                        let background = createBackground gherkinBackground 
                        background |> featureType.AddMember
                        ProvidedProperty("Background",background.AsType(),isStatic = false, getterCode = fun _ -> <@@ obj() @@> )  |> featureType.AddMember
                        
                | :? Ast.Scenario ->
                        let gherkinScenario = c :?> Ast.Scenario
                        let scenarioName = gherkinScenario.Name

                        if (gherkinScenario.Examples |> Seq.isEmpty)
                        then

                            let scenario = createScenario gherkinScenario
                            scenario |> scenarios.AddMember
                            ProvidedProperty(scenarioName,scenario.AsType(),isStatic = false, getterCode=fun _ -> <@@obj()@@>) |> scenarios.AddMember
                        else
                            let scenarioOutline = createScenario gherkinScenario
                            let exampleType = createExampleType gherkinScenario
                            let examplesType = (typedefof<seq<_>>).MakeGenericType(exampleType.AsType())


                            let examples = createExampleInstances gherkinScenario exampleType

                            let examplesProp = ProvidedProperty("Examples",examplesType,getterCode=fun _ -> examples )

                            exampleType |> scenarioOutline.AddMember
                            examplesProp |> scenarioOutline.AddMember
                            
                            scenarioOutline  |> scenarioOutlines.AddMember
                        

                            ProvidedProperty(scenarioName,scenarioOutline.AsType(),isStatic = false, getterCode=fun _ -> <@@obj()@@>) |> scenarioOutlines.AddMember
                            
                | _ -> ()
        )

        scenarios |> featureType.AddMember
        scenarioOutlines |> featureType.AddMember

        ProvidedProperty("Scenarios",scenarios.AsType(),isStatic = false, getterCode = fun _ -> <@@ obj() @@> )  |> featureType.AddMember
        ProvidedProperty("ScenarioOutlines",scenarioOutlines.AsType(),isStatic = false, getterCode = fun _ -> <@@ obj() @@> )  |> featureType.AddMember
        ProvidedProperty("FeatureName",typeof<string>,isStatic = false, getterCode = fun _ -> <@@ featureName @@> )  |> featureType.AddMember
        ProvidedProperty("FeatureDescription",typeof<string>,isStatic = false, getterCode = fun _ -> <@@ featureDesc @@> )  |> featureType.AddMember

        let featureProp = ProvidedProperty(featureName,featureType.AsType(),isStatic=true,getterCode=fun _ -> <@@ obj() @@>) 
        
        featureType |> root.AddMember
        featureProp |> root.AddMember

        providedAssembly.AddTypes [root]

        root

    do
        let gherkinProvider = ProvidedTypeDefinition(asm, ns, "GherkinProvider", None, hideObjectMethods=true, nonNullable=true, isErased=false)
        
        gherkinProvider.DefineStaticParameters( 
                [ProvidedStaticParameter("Feature path", typeof<string>)], 
                fun providerName args -> createFeature providerName (unbox<string> args.[0]))
        this.AddNamespace(ns,[gherkinProvider])
        

[<assembly:CompilerServices.TypeProviderAssembly()>]
do()