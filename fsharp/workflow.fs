type WorkflowCommand<'TOutput> =
    | Send of message: 'TOutput
    | Complete

type WorkflowCommand<'TOutput> =
    | Reply of 'TOutput
    | Send of 'TOutput
    | Publish of 'TOutput
    | Schedule of after: TimeSpan * message: 'TOutput
    | Complete

type Workflow<'TInput,'TState,'TOutput> = {
    Decide: 'TInput -> 'TState -> WorkflowCommand<'TOutput> list
}

type Workflow<'TInput,'TState,'TOutput> = {
    InitialState: 'TState
    Decide: 
        'TInput 
        -> 'TState 
        -> WorkflowCommand<'TOutput> list
}

type Workflow<'TInput,'TState,'TOutput> = {
    InitialState: 'TState
    Decide: 
        WorkflowTrigger<'TInput> 
        -> 'TState 
        -> WorkflowCommand<'TOutput> list
}

module IssueTrafficFineForSpeedingViolationWorkflow =
    let evolve state message =
        match (state, message) with
        | Initial, InitiatedBy(PoliceReportPublished m) ->
            match m.Offense with
            | SpeedingViolation v -> 
                AwaitingSystemNumber { 
                  PoliceReportId = m.PoliceReportId }
            | ParkingViolation v -> Final
        | AwaitingSystemNumber s,
            Received(TrafficFineSystemNumberGenerated m) ->
            AwaitingManualIdentificationCode {
              PoliceReportId = s.PoliceReportId
              SystemNumber = m.Number }
        | AwaitingManualIdentificationCode _,
            Received(TrafficFineManualIdentificationCodeGenerated _) ->
            Final
        | _, _ -> 
          failwithf "%A not supported by %A" message state

type Workflow<'TInput,'TState,'TOutput> = {
    InitialState: 'TState
    Evolve: 'TState -> WorkflowEvent<'TInput, 'TOutput> -> 'TState
    Decide: 'TInput -> 'TState -> WorkflowCommand<'TOutput> list
}

type WorkflowEvent<'TInput, 'TOutput> =
    | Began
    | InitiatedBy of 'TInput
    | Received of 'TInput
    | Replied of 'TOutput
    | Sent of 'TOutput
    | Published of 'TOutput
    | Scheduled of after: TimeSpan * message: 'TOutput
    | Completed

[
    Began
    InitiatedBy(PoliceReportPublished { 
        PoliceReportId = "XG.96.L1.5000267/2023"
        Offense = SpeedingViolation { MaximumSpeed = "50km/h" } })
    Sent(GenerateTrafficFineSystemNumber { 
        PoliceReportId = "XG.96.L1.5000267/2023"})

    Received(TrafficFineSystemNumberGenerated { 
        PoliceReportId = "XG.96.L1.5000267/2023"
        Number = "PPXRG/23TV8457" })
    Sent(GenerateTrafficFineManualIdentificationCode { 
        PoliceReportId = "XG.96.L1.5000267/2023"
        SystemNumber = "PPXRG/23TV8457" })

    Received(TrafficFineManualIdentificationCodeGenerated { 
        PoliceReportId = "XG.96.L1.5000267/2023"
        Number = "PPXRG/23TV8457"
        Code = "XMfhyM" })
    Sent(IssueTrafficFine { 
        PoliceReportId = "XG.96.L1.5000267/2023"
        SystemNumber = "PPXRG/23TV8457"
        ManualIdentificationCode = "XMfhyM" })
    Completed
]

let translate begins message commands =
    [
        if begins then
            yield Began
            yield InitiatedBy message
        else
            yield Received message

        for command in commands do
            yield 
                match command with
                | Reply m -> Replied m
                | Send m -> Sent m
                | Publish m -> Published m
                | Schedule (t, m) -> Scheduled (t, m)
                | Complete m -> Completed m
    ]