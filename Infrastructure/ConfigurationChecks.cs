using System.Text.Json;
namespace CfcPilot;
public static class ConfigurationChecks
{
    public static void Run()
    {
        var passed=0;var admin=new User{Id="admin",Name="Admin teste",Role="Administrador"};var clerk=new User{Id="clerk",Role="Atendente"};
        JsonElement Body(object value)=>JsonSerializer.SerializeToElement(value,Store.Json);
        void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);passed++;Console.WriteLine("PASS "+label);}
        void Refused(Action action,int status,string label){try{action();throw new InvalidOperationException(label);}catch(RuleException ex){Check(ex.Status==status,label);}}
        var s=Seed.Create();var past=Operations.Now.Date.AddDays(-1);while(past.DayOfWeek==DayOfWeek.Sunday)past=past.AddDays(-1);past=past.AddHours(9);
        object LessonBody(DateTime date,string reason="")=>new{enrollmentId="e3",instructorId="i2",vehicleId="v2",start=date.ToString("s"),reason};
        Refused(()=>Operations.Execute(s,admin,"lesson",Body(LessonBody(past))),403,"Retroatividade bloqueada por padrão");
        s.Policy.RetroactiveLessons="Administrador";s.Policy.RetroactiveExams="Administrador";s.Policy.RetroactiveEntries="Administrador";
        Refused(()=>Operations.Execute(s,clerk,"lesson",Body(LessonBody(past,"Conferência de histórico"))),403,"Atendente não usa permissão exclusiva do administrador");
        Refused(()=>Operations.Execute(s,admin,"lesson",Body(LessonBody(past))),400,"Retroatividade exige justificativa");
        var lessons=(List<Lesson>)Operations.Execute(s,admin,"lesson",Body(LessonBody(past,"Conferência de histórico")));Check(lessons[0].Start==past.ToString("s"),"Aula retroativa respeita a data informada");
        Refused(()=>Operations.Execute(s,admin,"lesson",Body(LessonBody(past,"Tentativa duplicada"))),409,"Aula idêntica não é gravada duas vezes");
        var second=past.AddMinutes(15);s.Policy.EnforceStudentConflict=false;s.Policy.EnforceInstructorConflict=false;s.Policy.EnforceVehicleConflict=false;
        Check(((List<Lesson>)Operations.Execute(s,admin,"lesson",Body(LessonBody(second,"Exceção autorizada pelo teste")))).Count==1,"Sobreposição depende das regras configuradas e da justificativa");
        s.Policy.MaxRetroactiveDays=2;Refused(()=>Operations.Execute(s,admin,"lesson",Body(LessonBody(past.AddDays(-30),"Conferência de histórico"))),409,"Prazo retroativo configurado é aplicado");s.Policy.MaxRetroactiveDays=0;
        var exam=(Exam)Operations.Execute(s,admin,"exam",Body(new{enrollmentId="e3",type="Médico",start=past.ToString("s"),location="Clínica de teste",reason="Registro histórico conferido"}));Check(exam.Start==past.ToString("s"),"Exame retroativo configurável");
        var year=Operations.Now.Year-1;var payment=(Entry)Operations.Execute(s,admin,"receive",Body(new{enrollmentId="e3",amount=1,method="PIX",description="Recibo histórico de teste",date=year+"-06-10",reason="Registro histórico conferido"}));Check(payment.Date==year+"-06-10","Pagamento conserva data retroativa");
        var real=Seed.Create();real.RealData=true;real.Policy.RetroactiveEntries="Administrador";var receipt=(Entry)Operations.Execute(real,admin,"receive",Body(new{enrollmentId="e3",amount=1,method="PIX",description="Recibo histórico de teste",date=year+"-06-10",reason="Registro histórico conferido"}));Check(receipt.Receipt.Contains("-"+year+"-"),"Recibo usa sequência do ano do lançamento");
        Refused(()=>Operations.Execute(s,admin,"entry",Body(new{unitId="u1",kind="Receita",amount=1,method="PIX",description="Teste futuro",date=Operations.Now.AddDays(2).ToString("yyyy-MM-dd")})),409,"Lançamento futuro segue opção própria");
        Refused(()=>Operations.Execute(s,clerk,"policy",Body(s.Policy)),403,"Somente administrador muda políticas");
        s.Policy.MaxDaily=100;s.Policy.Minutes=15;Check(((Policy)Operations.Execute(s,admin,"policy",Body(s.Policy))).MaxDaily==100,"Limites e duração ampliados configuráveis");
        var profile=(BrowserProfile)Operations.Execute(s,admin,"student-browser-profile",Body(new{studentId="s3"}));Check(profile.Kind=="Aluno"&&profile.StudentId=="s3","Sessão GOV.BR vinculada a um único aluno");
        Check(((BrowserProfile)Operations.Execute(s,admin,"student-browser-profile",Body(new{studentId="s3"}))).Id==profile.Id,"Sessão do aluno é reutilizada sem duplicação");
        var rows=new List<Lesson>{new(){Id="1",StudentId="s3",InstructorId="i2",VehicleId="v2",Start=past.ToString("s")},new(){Id="2",StudentId="s3",InstructorId="i2",VehicleId="v2",Start=past.ToString("s")}};
        Check(AgendaReview.Scan(rows,["s3"],["i2"],["v2"]).Count==1,"Auditoria conta uma duplicata sem triplicar por recurso");rows[1].ExcludedFromAgenda=true;Check(AgendaReview.Scan(rows,["s3"],["i2"],["v2"]).Count==0,"Registro auxiliar vinculado sai dos conflitos operacionais");
        rows[1].ExcludedFromAgenda=false;rows[1].StudentId="s4";foreach(var row in rows){row.Imported=true;row.SourceId="same-session";}Check(AgendaReview.Scan(rows,["s3","s4"],["i2"],["v2"]).Count==0,"Participantes da mesma sessão importada não geram conflito artificial de recurso");
        Check(PortalExtraction.ContainsCpf("CPF: 123.456.789-09","12345678909"),"Identidade visível com CPF formatado");
        Check(PortalExtraction.ContainsCpf("CPF: 12345678909","12345678909"),"Identidade visível com CPF sem máscara");
        Check(!PortalExtraction.ContainsCpf("Protocolo 123456 — data 78909","12345678909"),"Números separados não simulam identidade do aluno");
        Check(!PortalExtraction.ContainsCpf("Protocolo 0123456789090","12345678909"),"CPF não é aceito dentro de um número maior");
        Console.WriteLine(passed+" verificações de configuração e agenda aprovadas.");
    }
}
