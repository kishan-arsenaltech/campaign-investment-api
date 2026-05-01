using System.ComponentModel;

namespace Investment.Core.Dtos
{
    public enum InvestmentStage
    {
        Private = 1,

        Public = 2,

        [Description("Closed - Invested")]
        ClosedInvested = 3,

        [Description("Closed - Not Invested")]
        ClosedNotInvested = 4,

        New = 5,

        [Description("Compliance Review")]
        ComplianceReview = 6,

        [Description("Completed - Ongoing")]
        CompletedOngoing = 7,

        Vetting = 8,

        [Description("Completed - Ongoing/Private ")]
        CompletedOngoingPrivate = 9,
    }
}
