using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Equiv.Samples.BusinessLayer
{
    public class OrderService
    {
        private readonly object gate = new object();
        private int processed;
        private int lastConfirmedId;

        public string CustomerName(Order order)
        {
            return order.CustomerName;
        }

        public decimal Subtotal(Order order)
        {
            decimal subtotal = 0m;
            foreach (OrderLine line in order.Lines)
            {
                subtotal += line.UnitPrice * line.Quantity;
            }

            return subtotal;
        }

        public List<string> SkusOver(Order order, int minQuantity)
        {
            return order.Lines.Where(line => line.Quantity > minQuantity).Select(line => line.Sku).ToList();
        }

        public int ParseQuantity(string text)
        {
            return int.TryParse(text, out var quantity) ? quantity : 0;
        }

        public async Task ConfirmAsync(Task<Order> pending)
        {
            Order order = await pending;
            lastConfirmedId = order.Id;
        }

        public int QuantityOf(object item)
        {
            if (item is OrderLine line)
            {
                return line.Quantity;
            }

            return 0;
        }

        public string Describe(Order order)
        {
            return $"Order {order.Id} for {order.CustomerName}";
        }

        public string Export(Order order)
        {
            using (StringWriter writer = new StringWriter())
            {
                writer.Write(order.Id);
                return writer.ToString();
            }
        }

        public int Record()
        {
            lock (gate)
            {
                processed = processed + 1;
                return processed;
            }
        }

        public bool IsLarge(int quantity)
        {
            return quantity > 100;
        }

        public int TotalQuantity(Order order)
        {
            int total = 0;
            foreach (OrderLine line in order.Lines)
            {
                total += line.Quantity;
            }

            return total;
        }

        public decimal LineTotal(OrderLine line)
        {
            return line.UnitPrice * line.Quantity - line.Discount;
        }

        public int Reserve(Order order, int quantity)
        {
            if (order == null)
            {
                throw new ArgumentNullException(nameof(order));
            }

            return quantity * 2;
        }

        public decimal RoundTotal(decimal total)
        {
            return Math.Round(total, 2);
        }
    }
}
