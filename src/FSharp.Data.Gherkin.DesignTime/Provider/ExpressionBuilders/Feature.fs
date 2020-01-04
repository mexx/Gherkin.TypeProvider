module ExpressionBuilders.Feature

open ObjectModel
open ExpressionBuilders.ScenarioContainer
open ExpressionBuilders.TagContainer
open ExpressionBuilders.Background
open ExpressionBuilders.RuleContainer
open ProviderImplementation.ProvidedTypes
open Gherkin.Ast
open FSharp.Quotations
open Shared

let sanitize = Sanitizer().Sanitize

type FeatureExpressionBuilder  
            (backgroundExpression:BackgroundExpressionBuilder,
            scenarioContainerExpressionBuilder:ScenarioContainerExpressionBuilder,
            ruleContainerExpressionBuilder:RuleContainerExpressionBuilder,
            tagContainerExpressionBuilder:TagContainerExpressionBuilder) =

    let getFeatureItemsFromDocument (document:GherkinDocument) =
        let scenarios =
            document.Feature.Children |> Seq.toList
            |> List.choose(
                fun c ->
                    match c with
                    | :? Scenario -> Some  (c :?> Scenario)
                    | _ -> None)
            
        let rules =
            document.Feature.Children |> Seq.toList
            |> List.choose(
                fun c ->
                    match c with
                    | :? Rule -> Some  (c :?> Rule)
                    | _ -> None)

        let background = 
            document.Feature.Children
            |> Seq.tryPick(
                fun c ->
                    match c with
                    | :? Background -> Some  (c :?> Background)
                    | _ -> None)

        background,scenarios,rules,(document.Feature.Tags |> Seq.toList |> List.map(fun t -> t.Name))

    let getBackgroundExpression (feature:ProvidedTypeDefinition) (background:Background option) =
            match background with
            | None -> None
            | Some b -> 
                let backgroundExpresssion =backgroundExpression.CreateExpression feature b
                let backgroundField = PropertyHelper(feature).AddProperty("Background",backgroundExpresssion.Type)
                Some (backgroundExpresssion,backgroundField)

    member __.CreateExpression (providerName:string) (root:ProvidedTypeDefinition) (document:GherkinDocument) = 

            let (background,scenarios,rules,tags) = getFeatureItemsFromDocument document 
            let featureName = sprintf "%s_Feature" providerName
            let featureType= ProvidedTypeDefinition(featureName,Some typeof<obj>,isErased=false, hideObjectMethods=true, isSealed=false)

            //Add Feature
            featureType |> root.AddMember

            let propertyHelper = PropertyHelper(featureType)

            // add name & description
            let nameField = propertyHelper.AddProperty("Name",typeof<string> )
            let descriptionField = propertyHelper.AddProperty("Description",typeof<string>)

            //get background if it exists
            let backgroundExpression = getBackgroundExpression featureType background

            // create scenarios container
            let scenarioContainerExpression = scenarioContainerExpressionBuilder.CreateExpression featureType scenarios
            let scenarioContainerField = propertyHelper.AddProperty("Scenarios",scenarioContainerExpression.Type)
            
            // create rules container
            let rulesContainerExpression = ruleContainerExpressionBuilder.CreateExpression featureType rules
            let rulesContainerField = propertyHelper.AddProperty("Rules",rulesContainerExpression.Type)

            //create tags
            let tagContainerExpression = tagContainerExpressionBuilder.CreateExpression featureType tags
            let defaultTagContainer =
                    match tagContainerExpression with
                    | Some _ -> None  
                    | None -> Some (tagContainerExpressionBuilder.CreateDefaultTagContainer featureType)
            
            //create the hastag method
            let hasTagMethod =
                    ProvidedMethod(
                        "HasTag",
                        [ProvidedParameter("tagName",typeof<string>)],
                        typeof<bool>,
                        isStatic = false,
                        invokeCode = fun _ ->
                            let hasTags = tagContainerExpression.IsSome
                            <@@ hasTags  @@>)
                    
            hasTagMethod |> featureType.AddMember
                        
            //add the optional parameters to mandatory parameters
            let parameters = 
                let mandatory = [
                    ProvidedParameter("name",typeof<string>)
                    ProvidedParameter("description",typeof<string>)
                    ProvidedParameter("scenarios",scenarioContainerExpression.Type)
                    ProvidedParameter("rules",rulesContainerExpression.Type)
                ]
                match backgroundExpression,tagContainerExpression with
                | None,None -> mandatory
                | Some (backgroundType,_),None -> mandatory @ [ProvidedParameter("background",backgroundType.Type) ]
                | None,Some(tagType,_) -> mandatory @ [ProvidedParameter("tags",tagType)]
                | Some (backgroundType,_),Some(tagType,_) -> mandatory @ [ProvidedParameter("background",backgroundType.Type);ProvidedParameter("tags",tagType)]

            ProvidedConstructor(
                parameters,
                invokeCode = (fun args ->
                    let this = args.[0]

                    //set the mandatory & description fields
                    let setName = Expr.FieldSet(this,nameField, args.[1])
                    let setDescription = Expr.FieldSet(this,descriptionField,args.[2])
                    let setScenarios = Expr.FieldSet(this,scenarioContainerField,args.[3])
                    let setRules = Expr.FieldSet(this,rulesContainerField,args.[4])
                    let setMandatory =
                        [
                            setDescription;setScenarios;setRules
                        ] |> List.fold(fun a c -> Expr.Sequential(a,c)) setName
                    
                    // set empty tag collection of none exists
                    let setEmptyTags defaultTagContainer =
                            match defaultTagContainer with
                            | None -> []
                            | Some (defaultContainer:ProvidedField,tagBaseType) ->
                                let emptyTags= Expr.NewArray(tagBaseType,[])
                                let emptyTagContainer =Expr.NewObject(defaultContainer.FieldType.GetConstructors().[0],[emptyTags])
                                let setTags = Expr.FieldSet(this,defaultContainer,emptyTagContainer)
                                [setTags]

                    // add any background and tags
                    let additionalSets =
                        match backgroundExpression,tagContainerExpression with
                        | None,None -> setEmptyTags defaultTagContainer
                        | Some(_,backgroundField),None -> Expr.FieldSet(this,backgroundField,args.[5]) :: (setEmptyTags defaultTagContainer)
                        | Some(_,backgroundField),Some(_,tagField) -> [Expr.FieldSet(this,backgroundField,args.[5]);Expr.FieldSet(this,tagField,args.[6])]
                        | None,Some(_,tagField) ->  [Expr.FieldSet(this,tagField,args.[5])]
                        
                    additionalSets |> Seq.fold(fun a c -> Expr.Sequential(a,c)) setMandatory

                    )) |> featureType.AddMember

            {
                Name = "Feature"
                Type = featureType
                Scenarios = scenarioContainerExpression
                Rules = rulesContainerExpression
                Background = match backgroundExpression with | None -> None | Some (backgroundExp,_) -> Some backgroundExp
                Tags = match tagContainerExpression with | None -> None | Some (tagExpr,_) -> Some tagExpr
            }

    static member CreateNew (providerModel:GherkinProviderModel) (propertyNameSanitizer:string->string) =

        FeatureExpressionBuilder
            (BackgroundExpressionBuilder.CreateNew providerModel propertyNameSanitizer,
            ScenarioContainerExpressionBuilder.CreateNew providerModel propertyNameSanitizer,
            RuleContainerExpressionBuilder.CreateNew providerModel propertyNameSanitizer,
            TagContainerExpressionBuilder.CreateNew providerModel)