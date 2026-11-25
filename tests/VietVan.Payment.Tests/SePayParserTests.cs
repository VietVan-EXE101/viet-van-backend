using System.Text.Json;
using VietVan.API.Services.Payments;
using Xunit;

namespace VietVan.Payment.Tests;
public class SePayParserTests
{
    private const string Code="VV0123456789ABCDEF";
    private static string Payload(string content,string code="",int amount=99000,string type="in") =>
        JsonSerializer.Serialize(new {id=92704,gateway="Vietcombank",accountNumber="1234567890",
            content,code,transferType=type,transferAmount=amount});

    [Fact] public void Extract_Valid_Code_From_Content()
    {
        var r=SePayWebhookParser.Parse(Payload("Chuyen khoan "+Code));
        Assert.Equal(Code,SePayWebhookParser.ExtractOrderCode(r));
        Assert.Equal(99000,r.Amount);
    }
    [Fact] public void Extract_Rejects_Two_Order_Codes()
    {
        var payload=Payload(Code+" VVFFFFFFFFFFFFFFFF");
        Assert.Null(SePayWebhookParser.ExtractOrderCode(SePayWebhookParser.Parse(payload)));
    }
    [Fact] public void Extract_Rejects_Conflicting_Code_Field()
    {
        var payload=Payload(Code,"VVFFFFFFFFFFFFFFFF");
        Assert.Null(SePayWebhookParser.ExtractOrderCode(SePayWebhookParser.Parse(payload)));
    }
    [Fact] public void Parse_Rejects_Missing_Amount()
    {
        Assert.Throws<FormatException>(()=>SePayWebhookParser.Parse("{\"id\":1}"));
    }
    [Fact] public void Missing_Currency_Defaults_To_Vnd()
    {
        Assert.Equal("VND",SePayWebhookParser.Parse(Payload(Code)).Currency);
    }
    [Fact] public void Rejects_OrderCode_Embedded_In_Another_Word()
    {
        Assert.Null(SePayWebhookParser.ExtractOrderCode(SePayWebhookParser.Parse(Payload("X"+Code+"Z"))));
    }
}
