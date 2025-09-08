namespace Microsoft.eShopWeb.Web.Pages.Basket;

public class ReserveOrderViewModel
{
    public string UserId { get; set; }
    public List<ReserveItemsViewModel> Items { get; set; }
}

public class ReserveItemsViewModel
{
    public string ItemId { get; set; }
    public int Quantity { get; set; }
}
