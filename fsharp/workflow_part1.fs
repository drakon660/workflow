module IssueTrafficFineForSpeedingViolationWorkflow =
    let decide message state =
        match (message, state) with
        | PoliceReportPublished m, Initial ->
            match m.Offense with
            | SpeedingViolation _ -> 
                [ Send(
                    GenerateTrafficFineSystemNumber { 
                      PoliceReportId = m.PoliceReportId }) ]
            | _ -> [ Complete ]
        | TrafficFineSystemNumberGenerated m,
            AwaitingSystemNumber s ->
            [ Send(
                GenerateTrafficFineManualIdentificationCode
                  { PoliceReportId = s.PoliceReportId
                    SystemNumber = m.Number }
              ) ]
        | TrafficFineManualIdentificationCodeGenerated m,
            AwaitingManualIdentificationCode s ->
            [ Send(
                IssueTrafficFine
                  { PoliceReportId = s.PoliceReportId
                    SystemNumber = s.SystemNumber
                    ManualIdentificationCode = m.Code }
              )
              Complete ]
        | _, _ -> 
          failwithf "%A not supported by %A" message state