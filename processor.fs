module IssueTrafficFineForSpeedingViolationWorkflowProcessor =
    let private options =
        JsonFSharpOptions
            .Default()
            .WithUnionExternalTag()
            .WithUnionUnwrapRecordCases()
            .ToJsonSerializerOptions()

    let private envelop workflow_id message_id m =
        let json = JsonSerializer.Serialize(m, options)
        let envelope =
            PubsubMessage(Data = ByteString.CopyFromUtf8(json))
        envelope.Attributes.Add("workflow_id", workflow_id)
        envelope.Attributes.Add("message_id", message_id)
        envelope

    let handle clients workflow_id message_id command =
        task {
            match command with
            | Send(GenerateTrafficFineSystemNumber m) -> 
                let envelope = envelop workflow_id message_id m
                do! 
                    clients
                        .SystemNumberTopic
                        .PublishAsync(envelope) 
                    :> Task
            | Send(GenerateTrafficFineManualIdentificationCode m) -> 
                let envelope = envelop workflow_id message_id m
                do! 
                    clients
                        .ManualIdentificationCodeTopic
                        .PublishAsync(envelope) 
                    :> Task
            | Send(IssueTrafficFine m) -> 
                let envelope = envelop workflow_id message_id m
                do! 
                    clients
                        .IssueTrafficFineTopic
                        .PublishAsync(envelope) 
                    :> Task
            | _ -> 
                failwith 
                    "%A command has not been implemented." 
                    command
        }