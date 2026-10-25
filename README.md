# Transcritor de Reunião

Tomador de notas de reunião para Windows. Um cliente de bandeja detecta quando uma reunião começa
(Teams, Google Meet, Zoom), grava o áudio dos participantes e o seu microfone, transcreve **localmente**
com Whisper e deixa a transcrição disponível, em tempo real, para o Claude Desktop ou o Claude Code via
**MCP**, que redige a ata. Tudo roda no seu PC, dentro do plano do Claude, sem custo de API.

```
Teams / Meet / Zoom ──► loopback WASAPI ─┐
                                         ├─► detector de voz ─► fila ─► Whisper (local) ─► transcript.jsonl
Microfone ───────────► WASAPI capture ───┘                                                     │
                                                                                               ▼
   Cliente de bandeja (TranscritorDeReuniao.exe) ◄── canal nomeado ── MeetingNotesMcp.exe ◄── Claude
   detecta reunião · inicia/para · escolhe a pasta                     servidor MCP (stdio)
```

## Instalar

Requisitos: Windows 11 x64 (ou Server 2022+), CPU com AVX2 e o
[Visual C++ Redistributable 2022 x64](https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist).
Para compilar: .NET 10 SDK.

```powershell
git clone https://github.com/AlissonTexeiraDev/TranscritorDeReuniao.git
cd TranscritorDeReuniao
.\scripts\publish.ps1            # gera publish\TranscritorDeReuniao (self-contained, não exige .NET no destino)
.\scripts\install.ps1 -Startup   # instala para o usuário atual e inicia com o Windows
```

O `install.ps1` copia tudo para `%LOCALAPPDATA%\Programs\TranscritorDeReuniao`, cria o atalho no Menu
Iniciar, registra em *Aplicativos instalados* (desinstala com `install.ps1 -Uninstall`) e abre o cliente.
Não precisa de administrador.

Para distribuir um `Setup.exe`, instale o [Inno Setup](https://jrsoftware.org/isinfo.php)
(`winget install JRSoftware.InnoSetup`) e compile `installer\setup.iss` depois do `publish.ps1`.

## Como usar o cliente

O cliente fica na bandeja (ao lado do relógio). Ícone cinza = parado; vermelho = gravando.

- **Detecção automática** (ligada por padrão): a gravação começa sozinha alguns segundos depois que o
  Teams, um navegador (Meet) ou o Zoom começa a usar o microfone, e termina cerca de 20 s depois que o app
  solta o microfone. O título da reunião é lido do título da janela (Teams e Meet) quando possível.
- **Manual**: botão direito no ícone → *Iniciar gravação...* (pede o título) e *Parar gravação*. Ou os
  botões na janela principal (duplo clique no ícone).
- **Pasta das gravações**: na janela principal, *Alterar...*. Cada reunião vira uma subpasta.
- **Iniciar com o Windows** e **Configurações...** (modelo Whisper, idioma, dispositivos, nomes,
  sensibilidade de voz, apps monitorados, tempo para encerrar).

Na primeira gravação o modelo Whisper é baixado (`base` ≈ 148 MB, `small` ≈ 488 MB) para
`%LOCALAPPDATA%\MeetingNotesMcp\models`. Para português, **small** é o melhor custo-benefício em CPU.

Como a detecção funciona: o cliente consulta a cada 2 s quais processos têm uma sessão de captura de áudio
ativa no Windows. Se o processo (ou o pai dele, no caso do WebView do Teams e dos processos utilitários do
navegador) está na lista de apps monitorados, há reunião. Não usa nenhuma API do Teams ou do Google. Se o
seu Teams ou navegador tiver outro nome de processo, adicione em *Configurações → Apps que indicam reunião*.

## Conectar ao Claude

O servidor MCP é `MeetingNotesMcp.exe`, na mesma pasta do cliente. Quando o cliente está aberto, o servidor
só repassa os comandos para ele (quem grava é o cliente, e a transcrição aparece na janela). Se o cliente
não estiver aberto, o servidor grava sozinho, como antes.

**Claude Desktop**: edite `%APPDATA%\Claude\claude_desktop_config.json` (Configurações → Desenvolvedor →
Editar configuração) e adicione o bloco de [examples/claude_desktop_config.example.json](examples/claude_desktop_config.example.json)
com o caminho da sua instalação. Feche o Claude por completo (bandeja → Sair) e abra de novo.

**Claude Code**:

```bash
claude mcp add meeting-notes --scope user -- "%LOCALAPPDATA%\Programs\TranscritorDeReuniao\MeetingNotesMcp.exe" serve
```

ou copie [examples/.mcp.example.json](examples/.mcp.example.json) para `.mcp.json` na raiz do projeto.

Uso típico numa conversa: "inicia a gravação da reunião *Sprint 42*", "o que foi decidido até agora?",
"registra a ação: Fulano revisa o contrato até sexta", "encerra e gera a ata". O prompt `gerar_ata` traz o
roteiro da ata. No Claude Code, `/loop 5m` com "leia a transcrição nova com get_transcript e atualize a ata
com save_minutes" mantém a ata viva durante a reunião.

## Ferramentas MCP

| Ferramenta | O que faz |
|---|---|
| `start_meeting(title?)` | Inicia captura + transcrição (no cliente, se aberto). |
| `stop_meeting()` | Para, esvazia a fila e grava `transcript.md`. |
| `get_status()` | Estado, duração, fila, dispositivos e quem controla a gravação. |
| `get_transcript(since_seq, max_entries)` | Transcrição incremental: `[seq] HH:mm:ss Falante: texto`. |
| `add_marker(kind, text)` | Marca `decisao`, `acao`, `topico` ou `nota` com horário. |
| `save_minutes(markdown)` | Grava `ata.md` na pasta da reunião. |
| `list_meetings()` / `load_meeting(id)` | Reabre reuniões antigas. |
| `list_devices()` | Saídas e entradas de áudio ativas. |

Prompt: `gerar_ata`.

## Arquivos gerados

```
<pasta escolhida>\2026-10-24_14-30_planejamento-da-sprint-42\
  meeting.json       metadados
  transcript.jsonl   uma entrada por linha, gravada em tempo real
  transcript.md      transcrição formatada (gerada ao parar)
  ata.md             ata escrita pelo Claude via save_minutes
```

## Configuração

O cliente grava `%APPDATA%\TranscritorDeReuniao\settings.json` (e `app.log` ao lado). Variáveis de
ambiente `MEETING_NOTES_*` têm precedência, o que permite configurar o servidor MCP direto no Claude:

| Variável | Padrão | Descrição |
|---|---|---|
| `MEETING_NOTES_MODEL` | `base` | `tiny`, `base`, `small`, `medium`, `large`, `large-turbo`. |
| `MEETING_NOTES_LANGUAGE` | `pt` | Idioma do Whisper (`auto` detecta). |
| `MEETING_NOTES_OUTPUT` | `Documentos\MeetingNotes` | Pasta raiz das reuniões. |
| `MEETING_NOTES_MODELS_DIR` | `%LOCALAPPDATA%\MeetingNotesMcp\models` | Onde ficam os modelos ggml. |
| `MEETING_NOTES_SPEAKER` | saída padrão | Trecho do nome do dispositivo de saída a capturar. |
| `MEETING_NOTES_MIC` | mic padrão de comunicação | Trecho do nome do microfone, ou `none`. |
| `MEETING_NOTES_MY_NAME` / `MEETING_NOTES_OTHERS_NAME` | `Eu` / `Outros` | Rótulos das falas. |
| `MEETING_NOTES_THREADS` | metade dos núcleos (2 a 8) | Threads do whisper.cpp. |
| `MEETING_NOTES_VAD_THRESHOLD` | `0.01` | Energia mínima (RMS) para considerar fala. |
| `MEETING_NOTES_VOCABULARY` | Jira, deploy, sprint... | Nomes e termos que o Whisper deve reconhecer (vão no prompt inicial). |

## Desenvolvimento

```
src/MeetingNotes.Core     motor: captura, VAD, Whisper, sessão, detector, canal nomeado, configurações
src/MeetingNotes.Client   cliente de bandeja (WinForms)        → TranscritorDeReuniao.exe
src/MeetingNotes.Mcp      servidor MCP (stdio)                 → MeetingNotesMcp.exe
scripts/                  publish.ps1, install.ps1
installer/setup.iss       instalador Inno Setup
examples/                 configurações de exemplo para o Claude
```

```bash
dotnet build TranscritorDeReuniao.sln -c Release
MeetingNotesMcp.exe devices      # lista dispositivos
MeetingNotesMcp.exe test 20      # grava 20 s e imprime a transcrição, sem MCP nem cliente
```

Observações:

- O `nuget.config` da raiz usa só o nuget.org, para não depender de feeds privados da máquina.
- Mantenha o repositório em um caminho curto. Se o caminho das DLLs nativas em `runtimes\win-x64\` passar de
  260 caracteres, o Windows não as carrega (erro 206) e o whisper.cpp aborta.
- Em pastas sincronizadas (iCloud Drive, OneDrive) o `Directory.Build.props` manda `bin`/`obj` para
  `%LOCALAPPDATA%\TranscritorDeReuniao\artifacts`. Ainda assim, repositório Git em pasta sincronizada é
  frágil; prefira clonar em uma pasta comum, como `C:\Git`.
- GPU: os executáveis já incluem `Whisper.net.Runtime.Vulkan`, que usa a GPU (NVIDIA, AMD ou Intel) sem
  instalar nenhum toolkit; o runtime escolhido aparece no log (`runtime=Vulkan` ou `Cpu`). Com CUDA Toolkit
  instalado, `Whisper.net.Runtime.Cuda` (ou `Cuda12`) é um pouco mais rápido. CPU sem AVX: troque
  `Whisper.net.Runtime` por `Whisper.net.Runtime.NoAvx`.
- Modelos na CPU deste tipo de máquina (8 threads): `small` transcreve 10 s de áudio em ~2 s; `large-turbo`
  leva ~10 s, ou seja, não acompanha a conversa. Na GPU, `large-turbo` fica em tempo real.

## Limitações

- Separação de falantes é só "você" (microfone) vs. "outros" (loopback). Diarização entre os outros exige
  outro modelo (pyannote) ou serviço (Azure Speech, Deepgram, AssemblyAI).
- O loopback captura tudo que toca na saída de áudio, inclusive música e notificações. A NAudio 3 expõe
  loopback por processo (`WasapiRecorderBuilder`), uma evolução natural.
- A detecção depende do app usar o microfone. Uma reunião em que você entra sem microfone não é detectada;
  inicie manualmente.
- Whisper pode "inventar" texto em silêncio. Há filtros para os casos comuns, mas revise a ata.

## Aviso

Gravar reuniões exige informar os participantes. No Brasil, gravar conversa da qual você participa é legal,
mas a LGPD e as políticas da empresa pedem transparência. Avise no início da reunião.
