module IssueTrafficFineForSpeedingViolationWorkflow =
    let decide message state =
        match (message, state) with
        | PoliceReportPublished m, Initial ->
            match m.Offense with
            | SpeedingViolation _ ->
                (AwaitingSystemNumber { 
                    PoliceReportId = m.PoliceReportId },
                 [ Send(
                     GenerateTrafficFineSystemNumber { 
                       PoliceReportId = m.PoliceReportId }) ])
            | _ -> (Final, [ Complete ])
        | TrafficFineSystemNumberGenerated m,
            AwaitingSystemNumber s ->
            (AwaitingManualIdentificationCode
                { PoliceReportId = s.PoliceReportId
                  SystemNumber = m.Number },
             [ Send(
                 GenerateTrafficFineManualIdentificationCode
                   { PoliceReportId = s.PoliceReportId
                     SystemNumber = m.Number }
             ) ])
        | TrafficFineManualIdentificationCodeGenerated m,
            AwaitingManualIdentificationCode s ->
            (Final,
             [ Send(
                 IssueTrafficFine
                   { PoliceReportId = s.PoliceReportId
                     SystemNumber = s.SystemNumber
                     ManualIdentificationCode = m.Code }
               )
               Complete ])
        | _, _ -> 
          failwithf "%A not supported by %A" message state