namespace Microsoft.eShopWeb.ApplicationCore;
public class ApplicationConnectionStrings(string orderProcessorLink)
{
    public string OrderProcessorLink { get; set; } = orderProcessorLink;
}
