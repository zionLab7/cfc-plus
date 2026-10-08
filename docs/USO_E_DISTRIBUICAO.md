# Um app para todas as escolas

Todos usam o domínio central, por exemplo https://app.seudominio.com.br. A escola entrega seu ID, o usuário individual e a senha inicial. O titular troca a senha no primeiro acesso. O login escolhe o banco da escola no servidor, e o perfil encaminha para gestão ou portal pessoal. Filiais são selecionadas dentro da escola; não exigem outra instalação.

## Aluno e instrutor

Acesse /portal/ ou entre pela página principal: o perfil é encaminhado automaticamente. A escola cria o usuário vinculado à ficha do aluno/instrutor em Portais aluno e instrutor. Não existe cadastro público livre. O aluno acompanha agenda, processo, parcelas/documentos e solicitações; o instrutor acompanha suas aulas, registra presença/avaliações e acompanha respostas da escola conforme permissões.

No Android, abra o portal no Chrome e use Instalar aplicativo. No iPhone/iPad, abra no Safari → Compartilhar → Adicionar à Tela de Início. Ambos instalam o mesmo CFC+; o ID e perfil determinam o conteúdo. A página /install/ orienta o usuário. Esta versão usa PWA e não depende de publicação nas lojas.

## Gestão

Gerentes, administradores e atendentes entram na gestão pelo mesmo domínio/ID. Podem instalar a PWA no computador e celular. No Windows, o instalador Electron compartilhado oferece Conectar ao servidor: informe o domínio central HTTPS e entre com o ID da escola + usuário + senha. Mac/Linux usam navegador ou PWA. Não escolha servidor local para atendimento de um cliente da plataforma; modo local é ferramenta de desenvolvimento/implantação local planejada.

O executável é cliente do servidor central: desligar o computador de atendimento não desliga o servidor nem suas sessões GOV/DETRAN. Perfis governamentais ficam protegidos na pasta da escola e dependem também dos prazos de sessão do portal externo.

## Sessões e conexão

Uma conta ativa por origem no mesmo perfil do navegador. Para administrar escolas diferentes simultaneamente, use perfis de navegador separados. Abas antigas não ganham permissão de usar outro banco silenciosamente: o cliente envia o ID ao qual a tela está vinculada e o servidor recusa divergência.

A gestão pode guardar algumas operações offline, vinculadas à escola e ao usuário de origem. Elas só são confirmadas após validação no servidor ao reconectar. Portais, documentos e consultas externas precisam de conexão; cache da PWA não é acesso irrestrito aos dados de qualquer escola. Não compartilhe usuário pessoal nem máquina sem bloqueio.

O instalador ainda precisa de assinatura de código comercial para distribuição definitiva. Não instrua clientes a desativar proteção do Windows. Dados e credenciais nunca são incluídos no instalador ou na imagem Docker.
