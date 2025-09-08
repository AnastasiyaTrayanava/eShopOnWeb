using System;
using System.Configuration;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BasketAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Events;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderService : IOrderService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IRepository<Basket> _basketRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IMediator _mediator;

    private readonly string _orderProcessorLink;

    public OrderService(IRepository<Basket> basketRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Order> orderRepository,
        IUriComposer uriComposer, IMediator mediator,
        ApplicationConnectionStrings connectionStrings)
    {
        _orderRepository = orderRepository;
        _uriComposer = uriComposer;
        _basketRepository = basketRepository;
        _itemRepository = itemRepository;
        _mediator = mediator;

        _orderProcessorLink = connectionStrings.OrderProcessorLink;
    }

    public async Task CreateOrderAsync(int basketId, Address shippingAddress)
    {
        var basketSpec = new BasketWithItemsSpecification(basketId);
        var basket = await _basketRepository.FirstOrDefaultAsync(basketSpec);

        Guard.Against.Null(basket, nameof(basket));
        Guard.Against.EmptyBasketOnCheckout(basket.Items);

        var catalogItemsSpecification = new CatalogItemsSpecification(basket.Items.Select(item => item.CatalogItemId).ToArray());
        var catalogItems = await _itemRepository.ListAsync(catalogItemsSpecification);

        var items = basket.Items.Select(basketItem =>
        {
            var catalogItem = catalogItems.First(c => c.Id == basketItem.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            var orderItem = new OrderItem(itemOrdered, basketItem.UnitPrice, basketItem.Quantity);
            return orderItem;
        }).ToList();

        var order = new Order(basket.BuyerId, shippingAddress, items);

        await _orderRepository.AddAsync(order);
        await SendOrderDetailsToProcessor(order);
        OrderCreatedEvent orderCreatedEvent = new OrderCreatedEvent(order);
        await _mediator.Publish(orderCreatedEvent);
    }

    private async Task SendOrderDetailsToProcessor(Order order)
    {
        var finalPrice = Enumerable.Sum(order.OrderItems.Select(x => x.Units * x.UnitPrice));
        var orderDetails = new OrderDetails()
        {
            ShippingAddress = new ShippingAddress()
            {
                City = order.ShipToAddress.City,
                Country = order.ShipToAddress.Country,
                State = order.ShipToAddress.State,
                Street = order.ShipToAddress.Street,
                ZipCode = order.ShipToAddress.ZipCode
            },
            FinalPrice = finalPrice,
            Items = order.OrderItems.Select(x => new Item()
            {
                Count = x.Units, 
                Name = x.ItemOrdered.ProductName, 
                ItemId = x.ItemOrdered.CatalogItemId
            }).ToList()
        };

        var httpClient = new HttpClient();

        if (string.IsNullOrEmpty(_orderProcessorLink))
        {
            throw new ConfigurationErrorsException("No function app url was present in configuration.");
        }

        var message = new HttpRequestMessage()
        {
            Method = HttpMethod.Post,
            RequestUri = new Uri(_orderProcessorLink),
            Content = new StringContent(JsonSerializer.Serialize(orderDetails))
        };

        var request = await httpClient.SendAsync(message);

        if (!request.IsSuccessStatusCode)
        {
            throw new Exception("Something went wrong while sending request to Azure Function.");
        }
    }
}
