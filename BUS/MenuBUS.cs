using System;
using System.Collections.Generic;
using System.Linq;
using DAL.MODELS;

namespace BUS
{
    public class MenuBUS
    {
        // Return list of bill items for a table (join BillInfo -> Food)
        public List<MenuItemDTO> GetMenuListByTable(int tableId)
        {
            using (var db = new QLQAContextDB())
            {
                var bill = db.Bills.FirstOrDefault(b => b.idTable == tableId && b.status == 0);
                if (bill == null) return new List<MenuItemDTO>();

                var items = from bi in db.BillInfoes
                            where bi.idBill == bill.id
                            join f in db.Foods on bi.idFood equals f.id
                            select new MenuItemDTO
                            {
                                FoodId = f.id,
                                Name = f.name,
                                Price = f.price,
                                Count = bi.count,
                                Total = bi.count * f.price
                            };
                return items.ToList();
            }
        }

        // Add food to bill logic: create bill if none
        public void AddFoodToBill(int tableId, int foodId, int count)
        {
            using (var db = new QLQAContextDB())
            {
                var bill = db.Bills.FirstOrDefault(b => b.idTable == tableId && b.status == 0);
                if (bill == null)
                {
                    bill = new Bill { idTable = tableId, DateCheckIn = DateTime.Now, status = 0, discount = 0 };
                    db.Bills.Add(bill);
                    db.SaveChanges();
                }

                var billInfo = db.BillInfoes.FirstOrDefault(bi => bi.idBill == bill.id && bi.idFood == foodId);
                if (billInfo == null)
                {
                    billInfo = new BillInfo { idBill = bill.id, idFood = foodId, count = count };
                    db.BillInfoes.Add(billInfo);
                }
                else
                {
                    billInfo.count += count;
                }

                db.SaveChanges();

                // update table status
                var table = db.TableFoods.Find(tableId);
                if (table != null) table.status = "Có người";
                db.SaveChanges();
            }
        }

        // Remove food from bill (all quantity or decrement)
        public void RemoveFoodFromBill(int tableId, int foodId, int countToRemove)
        {
            using (var db = new QLQAContextDB())
            {
                var bill = db.Bills.FirstOrDefault(b => b.idTable == tableId && b.status == 0);
                if (bill == null) return;

                var billInfo = db.BillInfoes.FirstOrDefault(bi => bi.idBill == bill.id && bi.idFood == foodId);
                if (billInfo == null) return;

                billInfo.count -= countToRemove;
                if (billInfo.count <= 0)
                {
                    db.BillInfoes.Remove(billInfo);
                }

                db.SaveChanges();

                // if no more items, remove bill and set table empty
                var remaining = db.BillInfoes.FirstOrDefault(bi => bi.idBill == bill.id);
                if (remaining == null)
                {
                    db.Bills.Remove(bill);
                    var table = db.TableFoods.Find(tableId);
                    if (table != null) table.status = "Trống";
                    db.SaveChanges();
                }
            }
        }

        // Check out bill
        public bool CheckOut(int billId, int discount, int idStaff)
        {
            using (var db = new QLQAContextDB())
            {
                var bill = db.Bills.Find(billId);
                if (bill == null) return false;

                var items = from bi in db.BillInfoes
                            where bi.idBill == billId
                            join f in db.Foods on bi.idFood equals f.id
                            select new { bi.count, f.price };

                double total = 0;
                foreach (var it in items)
                    total += it.count * it.price;

                bill.TotalAmount = total;
                bill.FinalPrice = total * (1 - discount / 100.0);
                bill.discount = discount;
                bill.DateCheckOut = DateTime.Now;
                bill.status = 1;
                bill.idStaff = idStaff;

                // set table empty
                var table = db.TableFoods.Find(bill.idTable);
                if (table != null) table.status = "Trống";

                db.SaveChanges();
                return true;
            }
        }

        // Merge all items from fromTable into toTable (combine counts if same food)
        public bool MergeTable(int fromTableId, int toTableId)
        {
            if (fromTableId == toTableId) return false;
            using (var db = new QLQAContextDB())
            {
                var fromBill = db.Bills.FirstOrDefault(b => b.idTable == fromTableId && b.status == 0);
                if (fromBill == null) return false; // nothing to merge

                var toBill = db.Bills.FirstOrDefault(b => b.idTable == toTableId && b.status == 0);
                if (toBill == null)
                {
                    toBill = new Bill { idTable = toTableId, DateCheckIn = DateTime.Now, status = 0, discount = 0 };
                    db.Bills.Add(toBill);
                    db.SaveChanges();
                }

                // Move or merge bill info entries
                var fromItems = db.BillInfoes.Where(bi => bi.idBill == fromBill.id).ToList();
                foreach (var fi in fromItems)
                {
                    var existing = db.BillInfoes.FirstOrDefault(bi => bi.idBill == toBill.id && bi.idFood == fi.idFood);
                    if (existing != null)
                    {
                        existing.count += fi.count;
                        db.BillInfoes.Remove(fi);
                    }
                    else
                    {
                        // change ownership to toBill
                        fi.idBill = toBill.id;
                    }
                }

                db.SaveChanges();

                // remove fromBill if it has no items
                var remaining = db.BillInfoes.FirstOrDefault(bi => bi.idBill == fromBill.id);
                var fromTable = db.TableFoods.Find(fromTableId);
                var toTable = db.TableFoods.Find(toTableId);

                if (remaining == null)
                {
                    // remove the empty bill
                    db.Bills.Remove(fromBill);
                    // ensure from table marked empty
                    if (fromTable != null)
                        fromTable.status = "Trống";
                }

                // ensure destination table marked occupied
                if (toTable != null)
                    toTable.status = "Có người";

                db.SaveChanges();
                return true;
            }
        }
    }
}
