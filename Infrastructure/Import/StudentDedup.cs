using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
namespace CfcPilot;

// Consolidate only proven identities. Raw exports and the original projected profiles remain recoverable.
public static class StudentDedup
{
 public static bool ValidCpf(string value)
 {
  var digits=string.Concat(value.Where(char.IsAsciiDigit)).PadLeft(11,'0');
  if(digits.Length!=11||digits.Distinct().Count()==1)return false;
  for(var n=9;n<=10;n++){var sum=0;for(var i=0;i<n;i++)sum+=(digits[i]-'0')*(n+1-i);var check=sum*10%11;if(check==10)check=0;if(check!=digits[n]-'0')return false;}
  return true;
 }
 static string IdentityName(string value)=>string.Join(' ',new string(value.Normalize(NormalizationForm.FormD).Where(c=>CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark).ToArray()).ToUpperInvariant().Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries));
 public static string[] Sources(SqliteConnection db,string student)
 {
  var value=Database.Scalar(db,"SELECT json FROM entities WHERE kind='Students' AND id=$0",student);
  if(value=="")throw new RuleException("Aluno não encontrado.",404);
  var profile=JsonSerializer.Deserialize<Student>(value,Database.Compact)!;
  return profile.LegacySourceIds.Append(profile.SourceId).Where(s=>s!="").Distinct(StringComparer.Ordinal).ToArray();
 }
 public static object Run(SqliteConnection db)
 {
  if(Database.Scalar(db,"SELECT value FROM metadata WHERE key='studentDedupSchema'")=="1")return new{alreadyApplied=true};
  var students=Database.Rows(db,"SELECT json FROM entities WHERE kind='Students' AND json_extract(json,'$.imported')=1").Select(r=>JsonSerializer.Deserialize<Student>(r["json"]!,Database.Compact)!).ToList();
  int consolidated=0,groups=0,ambiguous=0,phoneDuplicates=0;
  using var tx=db.BeginTransaction();
  Database.Execute(db,"CREATE TABLE IF NOT EXISTS student_aliases(alias_id TEXT PRIMARY KEY,canonical_id TEXT NOT NULL,source_id TEXT NOT NULL,original_json TEXT NOT NULL,reason TEXT NOT NULL); CREATE INDEX IF NOT EXISTS ix_student_alias_canonical ON student_aliases(canonical_id);");
  foreach(var group in students.GroupBy(s=>string.Concat(s.Cpf.Where(char.IsAsciiDigit)).PadLeft(11,'0')).Where(g=>g.Count()>1))
  {
   var candidates=group.OrderByDescending(s=>long.TryParse(s.SourceId,out var n)?n:0).ThenBy(s=>s.Id,StringComparer.Ordinal).ToList();
   var names=candidates.Select(s=>IdentityName(s.Name)).Distinct(StringComparer.Ordinal).ToArray();
   var births=candidates.Select(s=>s.Birth).Distinct(StringComparer.Ordinal).ToArray();
   if(!ValidCpf(group.Key)||names.Length!=1||names[0]==""||births.Length!=1||!DateOnly.TryParseExact(births[0],"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _))
   {
    ambiguous++;
    foreach(var item in candidates)Database.Execute(db,"INSERT INTO import_issues(table_name,source_id,relation,target_id,detail) VALUES('Aluno',$0,'CPF repetido: revisão de identidade',$1,'CPF inválido, nome ou nascimento divergente. Cadastros mantidos separados; nenhuma dívida foi unida por suposição.')",item.SourceId,candidates[0].SourceId);
    continue;
   }
   var canonical=candidates[0];groups++;
   canonical.Cpf=group.Key;canonical.LegacyUnitIds=candidates.SelectMany(s=>s.LegacyUnitIds.Append(s.UnitId)).Where(s=>s!="").Distinct(StringComparer.Ordinal).ToList();
   canonical.LegacySourceIds=candidates.SelectMany(s=>s.LegacySourceIds.Append(s.SourceId)).Where(s=>s!="").Distinct(StringComparer.Ordinal).ToList();
   canonical.Phones=candidates.SelectMany(s=>s.Phones.Prepend(s.Phone)).Where(p=>p.Trim()!="").ToList();
   canonical.Notes=string.Join("\n\n",candidates.Where(s=>s.Notes!="").Select(s=>"[Cadastro Infor "+s.SourceId+"] "+s.Notes));
   foreach(var alias in candidates.Skip(1))
   {
    if(canonical.Email=="")canonical.Email=alias.Email;if(canonical.Address=="")canonical.Address=alias.Address;if(canonical.Rg=="")canonical.Rg=alias.Rg;
    if(canonical.Mother=="")canonical.Mother=alias.Mother;if(canonical.Father=="")canonical.Father=alias.Father;
    canonical.Ladv|=alias.Ladv;canonical.Blocked|=alias.Blocked;
    Database.Execute(db,"INSERT INTO student_aliases VALUES($0,$1,$2,$3,$4)",alias.Id,canonical.Id,alias.SourceId,JsonSerializer.Serialize(alias,Database.Compact),"CPF com dígitos verificadores válidos, mesmo nome normalizado e mesma data de nascimento.");
    Database.Execute(db,"UPDATE entities SET student_id=$0,json=json_set(json,'$.studentId',$0) WHERE student_id=$1 AND kind<>'Students'",canonical.Id,alias.Id);
    Database.Execute(db,"UPDATE entities SET json=json_set(json,'$.linkedId',$0) WHERE kind='Users' AND json_extract(json,'$.role')='Aluno' AND json_extract(json,'$.linkedId')=$1",canonical.Id,alias.Id);
    Database.Execute(db,"DELETE FROM entities WHERE kind='Students' AND id=$0",alias.Id);
    consolidated++;
   }
  }
  // Normalize duplicate contact formatting without changing the preserved raw phone rows.
  foreach(var student in students.Where(s=>Database.Count(db,"SELECT count(*) FROM entities WHERE kind='Students' AND id=$0",s.Id)==1))
  {
   var seen=new HashSet<string>(StringComparer.Ordinal);var phones=new List<string>();
   foreach(var phone in student.Phones.Prepend(student.Phone).Where(p=>p.Trim()!="")){var digits=string.Concat(phone.Where(char.IsAsciiDigit));var key=digits==""?phone.Trim():digits;if(seen.Add(key))phones.Add(phone);else phoneDuplicates++;}
   student.Phones=phones;student.Phone=phones.FirstOrDefault()??"";if(student.Cpf.Length is >0 and <11&&student.Cpf.All(char.IsAsciiDigit))student.Cpf=student.Cpf.PadLeft(11,'0');
   Database.SaveEntity(db,"Students",student);
  }
  var result=new{groups,consolidated,ambiguous,phoneDuplicates,rawPreserved=true};
  Database.Execute(db,"INSERT INTO metadata(key,value) VALUES('studentDedupSchema','1'),('studentDedupReport',$0)",JsonSerializer.Serialize(result,Database.Compact));
  tx.Commit();return result;
 }
}
