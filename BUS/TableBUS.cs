using System;
using System.Collections.Generic;
using System.Linq;
using DAL.MODELS;

namespace BUS
{
    public class TableBUS
    {
        public List<TableFood> LoadTableList()
        {
            using (var db = new QLQAContextDB())
            {
                // exclude locked tables whose name starts with prefix
                return db.TableFoods.Where(t => t.name != null && !t.name.StartsWith("[KHÓA] ")).ToList();
            }
        }

        public bool SwitchTable(int fromId, int toId)
        {
            if (fromId == toId) return false;
            using (var db = new QLQAContextDB())
            {
                var fromTable = db.TableFoods.Find(fromId);
                var toTable = db.TableFoods.Find(toId);
                if (fromTable == null || toTable == null) return false;

                // Find unpaid bills
                var billFrom = db.Bills.FirstOrDefault(b => b.idTable == fromId && b.status == 0);
                var billTo = db.Bills.FirstOrDefault(b => b.idTable == toId && b.status == 0);

                if (billFrom == null)
                {
                    // Nothing to move
                    return false;
                }

                if (billTo == null)
                {
                    // Simple move: assign billFrom to dest table
                    billFrom.idTable = toId;
                    toTable.status = "Có người";
                    fromTable.status = "Trống";
                }
                else
                {
                    // Destination already has a bill -> merge items from billFrom into billTo
                    var fromItems = db.BillInfoes.Where(bi => bi.idBill == billFrom.id).ToList();
                    foreach (var fi in fromItems)
                    {
                        var existing = db.BillInfoes.FirstOrDefault(bi => bi.idBill == billTo.id && bi.idFood == fi.idFood);
                        if (existing != null)
                        {
                            existing.count += fi.count;
                            db.BillInfoes.Remove(fi);
                        }
                        else
                        {
                            fi.idBill = billTo.id;
                        }
                    }

                    // remove fromBill if no items left
                    var remaining = db.BillInfoes.FirstOrDefault(bi => bi.idBill == billFrom.id);
                    if (remaining == null)
                    {
                        db.Bills.Remove(billFrom);
                    }

                    toTable.status = "Có người";
                    fromTable.status = "Trống";
                }

                db.SaveChanges();
                return true;
            }
        }

        // Ensure initial status values are set to 'Trống' if null/empty
        public void EnsureTablesDefaultStatus()
        {
            using (var db = new QLQAContextDB())
            {
                var tables = db.TableFoods.Where(t => string.IsNullOrEmpty(t.status)).ToList();
                if (!tables.Any()) return;
                foreach (var t in tables)
                {
                    t.status = "Trống";
                }
                db.SaveChanges();
            }
        }
    }
}
