using System.Text.Json;
namespace CfcPilot;
public static class PortalExtractionChecks
{
    public static void Run()
    {
        const string cpf="12345678909";
        JsonElement Evidence(string[][] rows)=>JsonSerializer.SerializeToElement(new{frames=new[]{new{tables=new[]{rows}}}});
        void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);Console.WriteLine("PASS "+message);}
        var valid=Evidence([["CPF","123.456.789-09"],["RENACH:","SP123456789"],["Processo","2026/000123"],["Nome","IGNORAR"]]);
        var result=PortalExtraction.Proposals(valid,cpf);
        Check(result.Count==2&&result["renach"]=="SP123456789"&&result["process"]=="2026/000123","Extração limitada aos campos identificados e ao CPF selecionado");
        Check(PortalExtraction.Proposals(valid,"98765432100").Count==0,"Outro CPF não gera proposta");
        Check(PortalExtraction.Proposals(Evidence([["CPF",cpf],["CPF","98765432100"],["RENACH","SP123456789"]]),cpf).Count==0,"Tabela com múltiplas identidades não gera proposta");
        Check(PortalExtraction.Proposals(Evidence([["CPF",cpf],["RENACH","SP123456789"],["RENACH","SP987654321"]]),cpf).Count==0,"Valores conflitantes não são importados");
        Check(PortalExtraction.Proposals(Evidence([["CPF",cpf],["RENACH","<script>alert(1)</script>"]]),cpf).Count==0,"Conteúdo fora do formato não gera proposta");
        Check(PortalExtraction.Proposals(Evidence([["CPF","9"+cpf+"1"],["RENACH","SP123456789"]]),cpf).Count==0,"CPF como fragmento de outro número não vincula consulta");
        var state=new State{Units=[new(){Id="test-unit"}],Enrollments=[new(){Id="test-enrollment",StudentId="test-student",UnitId="test-unit"}],Entries=[new(){Id="debt",Kind="Débito",EnrollmentId="test-enrollment",Amount=100}],Installments=[new(){Id="cancelled",EnrollmentId="test-enrollment",Amount=50,Status="Cancelada",Due="2020-01-01"},new(){Id="open",EnrollmentId="test-enrollment",Amount=50,Due="2021-01-01"}]};
        var manager=new User{Id="test-manager",Role="Administrador"};
        var received=(Entry)Operations.Execute(state,manager,"receive",JsonSerializer.SerializeToElement(new{enrollmentId="test-enrollment",amount=20,method="PIX",description="Pagamento de teste isolado"}));
        Check(state.Installments[0].Paid==0&&state.Installments[1].Paid==20,"Recebimento não aloca valores em parcelas canceladas");
        Operations.Execute(state,manager,"void",JsonSerializer.SerializeToElement(new{id=received.Id,reason="Reversão de teste isolado"}));
        Check(state.Installments.All(x=>x.Paid==0),"Estorno reverte a alocação conhecida sem alterar parcela cancelada");
        state.Entries.Add(new(){Id="imported",Imported=true,Kind="Recebimento",Amount=20});var rejected=false;
        try{Operations.Execute(state,manager,"void",JsonSerializer.SerializeToElement(new{id="imported",reason="Teste de histórico importado"}));}catch(RuleException ex){rejected=ex.Status==409;}
        Check(rejected&&!state.Entries.Last().Voided,"Estorno importado exige conciliação sem modificar histórico");
    }
}
