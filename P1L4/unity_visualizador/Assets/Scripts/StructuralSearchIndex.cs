using System;
using System.Collections.Generic;

// Identifier matching is independent of scene visibility and camera state.
public static class StructuralSearchIndex
{
    public sealed class Item
    {
        public string Kind, Id, Tag, SourceId, Building, Level;
        public int Target;
        public string Label => $"{Kind.ToUpperInvariant()} · ID {Id}" +
            (string.IsNullOrEmpty(Tag) || Tag == Id ? "" : " · " + Tag);
    }

    public static List<Item> Find(List<Item> items, string query, string kind)
    {
        query=(query??"").Trim();var exact=new List<Item>();var partial=new List<Item>();
        if(query.Length==0)return exact;
        foreach(var item in items)
        {
            if(!string.IsNullOrEmpty(kind) && item.Kind!=kind)continue;
            if(Equal(item.Id,query)||Equal(item.Tag,query)||Equal(item.SourceId,query))exact.Add(item);
            else if(Contains(item.Id,query)||Contains(item.Tag,query)||Contains(item.SourceId,query))partial.Add(item);
        }
        // Numeric 72 must not silently choose 272 when an exact ID 72 exists.
        var result=exact.Count>0?exact:partial;
        result.Sort((a,b)=> {int order=string.Compare(a.Kind,b.Kind,StringComparison.Ordinal);
            return order!=0?order:string.Compare(a.Id,b.Id,StringComparison.OrdinalIgnoreCase);});
        return result;
    }
    private static bool Equal(string a,string b)=>string.Equals(a,b,StringComparison.OrdinalIgnoreCase);
    private static bool Contains(string a,string b)=>a!=null && a.IndexOf(b,StringComparison.OrdinalIgnoreCase)>=0;
}
