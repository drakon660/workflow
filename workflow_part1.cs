using System;
using System.Collections.Generic;

namespace IssueTrafficFineForSpeedingViolationWorkflow
{
    public static class Workflow
    {
        public static List<Command> Decide(Message message, State state)
        {
            return (message, state) switch
            {
                (PoliceReportPublished m, Initial) when m.Offense is SpeedingViolation =>
                    new List<Command>
                    {
                        new Send(new GenerateTrafficFineSystemNumber
                        {
                            PoliceReportId = m.PoliceReportId
                        })
                    },

                (PoliceReportPublished, Initial) =>
                    new List<Command> { new Complete() },

                (TrafficFineSystemNumberGenerated m, AwaitingSystemNumber s) =>
                    new List<Command>
                    {
                        new Send(new GenerateTrafficFineManualIdentificationCode
                        {
                            PoliceReportId = s.PoliceReportId,
                            SystemNumber = m.Number
                        })
                    },

                (TrafficFineManualIdentificationCodeGenerated m, AwaitingManualIdentificationCode s) =>
                    new List<Command>
                    {
                        new Send(new IssueTrafficFine
                        {
                            PoliceReportId = s.PoliceReportId,
                            SystemNumber = s.SystemNumber,
                            ManualIdentificationCode = m.Code
                        }),
                        new Complete()
                    },

                _ => throw new InvalidOperationException($"{message} not supported by {state}")
            };
        }
    }

    // Message types
    public abstract record Message;
    public record PoliceReportPublished(string PoliceReportId, Offense Offense) : Message;
    public record TrafficFineSystemNumberGenerated(string Number) : Message;
    public record TrafficFineManualIdentificationCodeGenerated(string Code) : Message;

    // State types
    public abstract record State;
    public record Initial : State;
    public record AwaitingSystemNumber(string PoliceReportId) : State;
    public record AwaitingManualIdentificationCode(string PoliceReportId, string SystemNumber) : State;

    // Command types
    public abstract record Command;
    public record Send(object Message) : Command;
    public record Complete : Command;

    // Domain types
    public abstract record Offense;
    public record SpeedingViolation : Offense;

    // Command message types
    public class GenerateTrafficFineSystemNumber
    {
        public string PoliceReportId { get; set; }
    }

    public class GenerateTrafficFineManualIdentificationCode
    {
        public string PoliceReportId { get; set; }
        public string SystemNumber { get; set; }
    }

    public class IssueTrafficFine
    {
        public string PoliceReportId { get; set; }
        public string SystemNumber { get; set; }
        public string ManualIdentificationCode { get; set; }
    }
}
