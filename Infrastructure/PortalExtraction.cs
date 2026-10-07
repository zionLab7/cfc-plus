using System.Text.Json;
using System.Text.RegularExpressions;
namespace CfcPilot;
public static class PortalExtraction
{
    public static bool ContainsCpf(string text,string cpf) => cpf.Length==11&&Regex.Matches(text,@"(?<!\d)\d{3}\.?\d{3}\.?\d{3}-?\d{2}(?!\d)").Any(match=>string.Concat(match.Value.Where(char.IsDigit))==cpf);
    public static Dictionary<string,string> Proposals(JsonElement evidence,string cpf)
    {
        var values=new Dictionary<string,HashSet<string>>();
        foreach(var frame in evidence.GetProperty("frames").EnumerateArray())foreach(var table in frame.GetProperty("tables").EnumerateArray())
        {
            var identities=table.EnumerateArray().SelectMany(row=>row.EnumerateArray()).SelectMany(cell=>Regex.Matches(cell.GetString()??"",@"(?<!\d)\d{3}\.?\d{3}\.?\d{3}-?\d{2}(?!\d)").Select(match=>string.Concat(match.Value.Where(char.IsDigit)))).Distinct().ToList();
            if(identities.Count!=1||identities[0]!=cpf)continue;
            foreach(var row in table.EnumerateArray())
            {
                var cells=row.EnumerateArray().Select(c=>c.GetString()??"").ToArray();if(cells.Length!=2)continue;
                var label=RelationalStore.SearchText(cells[0]).Trim().TrimEnd(':').Trim();
                var key=label switch{"renach" or "numero renach" or "nº renach" or "n° renach"=>"renach","processo" or "numero do processo" or "nº processo"=>"process",_=>""};
                var value=cells[1].Trim();if(key==""||!Regex.IsMatch(value,@"^[A-Za-z0-9./-]{6,25}$"))continue;
                if(!values.TryGetValue(key,out var set))values[key]=set=[];set.Add(value);
            }
        }
        // Conflicting or unbound fields are not offered for import.
        return values.Where(x=>x.Value.Count==1).ToDictionary(x=>x.Key,x=>x.Value.Single());
    }
}
