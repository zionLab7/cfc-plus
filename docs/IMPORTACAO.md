# Importação de uma nova autoescola

## Formato suportado

A interface desta versão importa exports **Infor SQL/CSV pareados**, com o esquema usado pelo projetor. Não importa automaticamente qualquer planilha, outro fornecedor ou backup binário SQL Server. A projeção reconhece as tabelas mapeadas; referências sem destino e classificações desconhecidas são preservadas como pendências. Solicite um adaptador para outros formatos antes de enviar dados reais.

Estrutura dentro do ZIP, admitindo uma pasta externa com nome livre:

```text
01_BANCO_DE_DADOS_COMPLETO/
  import-manifest.json
  qualquer_modulo/
    Aluno.sql
    Aluno.csv
    ...demais tabelas exportadas...
```

`import-manifest.json`:

```json
{"version":1,"tables":[{"name":"Aluno","rows":123},{"name":"Unidade","rows":2}]}
```

Os números devem representar as contagens reais de **todas** as tabelas exportadas; o exemplo não é um pacote completo. A fonte do inventário é o exportador da escola. O resumo legado `RESUMO_IMPORTACAO*.md` também é aceito para compatibilidade. O schema de testes contém apenas nomes de tabelas/colunas, sem nenhum registro de cliente.

Cada SQL tem cabeçalho `INSERT INTO [dbo].[Tabela] ([Campo], ...) VALUES` e dados compatíveis com o leitor. O CSV UTF-8 usa ponto e vírgula, mesmas colunas e registros. SQL é interpretado como dados; nunca executamos seus comandos. NULL, campos vazios e valores literais são preservados. Células de credenciais da origem são protegidas no arquivo de legado; não são publicadas.

Limites: ZIP 512 MB, trechos 4 MB, até 4.096 entradas, extração total 8 GB e arquivo individual 512 MB. O servidor exige ao menos 9 GB livres antes de aceitar uma nova tentativa. Imports muito maiores ou arquivos individuais pesados exigem divisão/adaptador de streaming e dimensionamento; não aumente limites sem medir memória. Pode haver necessidade de revisão do esquema de outra versão do Infor.

## Envio, conferência e ativação

1. Entre como administrador e abra **Instalação e importação**.
2. Escolha o ZIP e envie. Se a rede falhar, atualize o andamento e escolha o mesmo arquivo para continuar do último trecho confirmado.
3. Aguarde a conferência. Há somente uma tentativa pendente/em processamento por instalação. O app roda o importador em processo separado, com tempo máximo de duas horas. Um pacote recusado aparece como Falhou; não derruba o atendimento nem ativa dados.
4. Revise totais, tabelas e pendências. O SQL/CSV é comparado e cada registro armazenado é relido para conferir o hash. O SQLite passa por integridade física e verificação de relações.
5. Ative somente antes de iniciar a operação, com a confirmação explícita da revisão. O sistema bloqueia substituição se houver alunos, agenda, lançamentos ou outros registros operacionais. Cadastros administrativos e auditoria de senha não bloqueiam o primeiro import.
6. A stack reinicia para abrir o novo manifesto. O administrador atual mantém sua senha; demais usuários importados ficam inativos, preservados para revisão de perfil, unidade e senha. Não use senhas antigas da origem como acesso comercial sem revisão.
7. Abra Dados importados para conferir todas as tabelas, campos e pendências. Confira amostras de alunos, matrículas, grades, débitos, pagamentos, parcelas e saldos por unidade antes de liberar a escola.

O relatório integral e o log do worker ficam no volume privado em `imports/<id>/report.json` e `import-queue/<tentativa>/worker.log`. O manifesto anterior é guardado junto à tentativa. O pacote SQL/CSV fica retido nessa pasta privada; defina retenção com o cliente e remova tentativas antigas somente após ter backup e validar a necessidade de recuperação.

A interface realiza carga inicial; não faz merge incremental, substituição de operação existente nem migração automática entre volumes. Uma migração de base já em uso exige janela de manutenção, cópia e conciliação específicas. Não envie a base pela conversa, GitHub ou imagem Docker.
