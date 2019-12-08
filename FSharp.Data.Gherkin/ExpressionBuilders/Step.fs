module ExpressionBuilders.Step

open ExpressionBuilders
open ExpressionBuilders.Shared
open ExpressionBuilders.Data
open ProviderImplementation.ProvidedTypes
open FSharp.Quotations
open Gherkin.Ast


let createStepExpression  (parent:ProvidedTypeDefinition) (position:int)  (gherkinStep:Step) =

    let stepName = (sprintf "%i %s" position gherkinStep.Text) |> SanitizeName
    let stepType = ProvidedTypeDefinition(stepName,Some (StepBaseType.Value.AsType()),isErased=false, hideObjectMethods=true)
    stepType |> parent.AddMember
   
    let argumentType =
        if isNull gherkinStep.Argument then None
        else
            match gherkinStep.Argument with
            | :? DocString -> Some (DocStringType DocStringArgumentType.Value)
            | :? DataTable -> 
                let dataTable = gherkinStep.Argument :?> DataTable
                let columnNames = (dataTable.Rows |> Seq.head).Cells |> Seq.toList |> List.map (fun c -> c.Value)
                let dataTableRowType = createDataExpression stepType columnNames
                
                Some (DataTableType (dataTableRowType))
            | _ -> None

    let argumentBackingField = 
        match argumentType with
        | Some argType ->
            let visitedProperty = ArgumentBaseType.Value.GetProperty("Visited")
            let (argumentField,argumentProperty) =
                match argType with
                | DocStringType docStringType ->
                    let argumentField = ProvidedField("_argument",docStringType)

                    argumentField,
                    ProvidedProperty(
                            "Argument",docStringType,
                            getterCode = fun args -> 
                                let argField = Expr.FieldGet(args.[0],argumentField)
                                Expr.Sequential(
                                   Expr.PropertySet(argField,visitedProperty,Expr.Value(true)),
                                   Expr.FieldGet(args.[0],argumentField)))

                | DataTableType (dataTableType) ->
                    let arrayType = dataTableType.MakeArrayType()
                    let argumentField = ProvidedField("_argument",arrayType)

                    argumentField,
                    ProvidedProperty(
                        "Argument",arrayType,
                        getterCode = fun args -> Expr.FieldGet(args.[0],argumentField))
            
            argumentField |> stepType.AddMember
            argumentProperty |> stepType.AddMember        

            Some argumentField
        | _ -> None

    let parameters = 
        let staticParameters =
            [
                ProvidedParameter("order",typeof<int>)
                ProvidedParameter("keyword",typeof<string>)
                ProvidedParameter("text",typeof<string>)
            ]
        match argumentType with
        | None -> ProvidedParameter("argument",ArgumentBaseType.Value)
        | Some (argType) ->
                match argType with
                | DocStringType docStringType -> ProvidedParameter("argument",docStringType)
                | DataTableType (dataTableType) -> 
                    ProvidedParameter("argument",dataTableType.MakeArrayType())
        :: staticParameters |> List.rev


    let baseCtr = StepBaseType.Value.GetConstructors().[0]
    let stepCtr =
        ProvidedConstructor(
            parameters,
            invokeCode =
                fun args ->
                    match argumentBackingField with
                    | None -> <@@ () @@>
                    | Some arg -> 
                        
                        Expr.FieldSet(args.[0],arg,args.[4])
        )
    stepCtr.BaseConstructorCall <- 
        fun args -> 
            // know what type of arg it is here!!!!
            // last args are doc string then datatable
            match argumentType with
            | None ->  baseCtr,[args.[0];args.[1];args.[2];args.[3];Expr.Value(null);Expr.Value(null)]
            | Some argType ->
                match argType with
                | DocStringType _ -> baseCtr,[args.[0];args.[1];args.[2];args.[3];args.[4];Expr.Value(null)]
                | DataTableType _ -> baseCtr,[args.[0];args.[1];args.[2];args.[3];Expr.Value(null);args.[4]]


    stepCtr |> stepType.AddMember

    {
        Name = gherkinStep.Text
        Type = stepType
        Position = position
        Argument = argumentType 
    }
