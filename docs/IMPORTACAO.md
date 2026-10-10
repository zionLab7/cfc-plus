# Importação por autoescola

1. O dono cadastra o ID em /platform/. O administrador da escola entra com ID, usuário e senha, troca a senha inicial e abre Instalação e importação.
2. Envia um ZIP com a pasta 01_BANCO_DE_DADOS_COMPLETO, SQL/CSV pareados e inventário. São aceitos .sql, .csv, .md e .json nessa estrutura; não envie executáveis nem arquivos privados alheios ao export.
3. O upload usa trechos autenticados de até 4 MB e pode ser retomado com o mesmo arquivo. Limite ZIP 512 MB, expansão 8 GB, arquivo 512 MB e 4096 entradas. Caminhos fora do staging, links e duplicados são recusados.
4. A conferência roda em processo separado e compara os registros SQL/CSV/inventário. No máximo duas conferências executam simultaneamente no servidor central; as demais aguardam sem bloquear outras escolas. Todas as tabelas originais ficam preservadas para consulta, inclusive dados sem correspondência operacional confirmada.
5. Revise totais, referências ausentes, agenda e financeiro. Uma divergência não autoriza inventar um vínculo ou cobrar novamente. Metadados de documento não substituem PDF/foto original.
6. A ativação exige escola operacionalmente vazia e confirmação explícita do administrador. Bases em uso não são substituídas pela interface. O administrador atual é preservado e os usuários importados ficam inativos até revisão e criação/troca de acesso pessoal.
7. A ativação recarrega somente os serviços da escola afetada; o processo central e outras escolas continuam funcionando. Aguarde alguns segundos e entre novamente. Cookies/perfis continuam na pasta da escola.

Os arquivos de staging, relatório e logs privados ficam em tenants/<id>/import-queue e imports. Toda chamada resolve o ID pelo cookie assinado; outra escola não pode consultar, descartar ou ativar esse job. Se a conferência falhar, a base anterior permanece ativa. Reinício durante conferência marca o job como interrompido. Preserve o pacote de origem para recuperação e confira o relatório antes de reenviar.

O formato Infor existente é o caminho implementado; não há importação universal de qualquer planilha sem mapeamento. Para outro fornecedor/formato, precisamos desenvolver e validar o adaptador com sua estrutura, sem misturar unidades de escolas distintas. O mesmo CPF pode existir em diferentes escolas; dentro de uma escola, filiais compartilham o cadastro e seus vínculos.

Migração Windows → Linux exige importar os dados e revalidar credenciais/perfis no servidor de destino. Arquivos DPAPI não são portáveis por simples cópia. Nunca envie base, exports, anexos ou chaves para o repositório GitHub.


A partir de 0.4.2, a projeção usa a chave única Pedido_num para preservar matrículas distintas e seus vínculos por aluno. Cadastros de aluno são consolidados somente com CPF de dígitos verificadores válidos, mesmo nome normalizado e mesma data de nascimento completa. O cadastro mais recente pelo identificador original é o principal; os demais perfis, IDs e todos os registros brutos são preservados. Cursos, etapas e consultas do legado abrangem todos esses IDs. Valores, IDs financeiros e unidades não são deduplicados por aparência. Identidades divergentes ficam separadas e sinalizadas no relatório.
