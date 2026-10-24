# MeetingNotesMcp

Tomador de notas de reunião para Windows, exposto como servidor **MCP** (stdio).
Captura o áudio que sai das caixas (os outros participantes do Teams, Meet, Zoom...) e o
microfone (você), transcreve localmente com **Whisper** e deixa a transcrição disponível para o
Claude Desktop ou o Claude Code em tempo real, dentro do seu plano, sem custo de API.

```
Teams/Meet/Zoom ──► loopback WASAPI ─┐
                                     ├─► VAD por energia ─► fila ─► Whisper.net (local) ─► transcript.jsonl
Microfone ─────────► WASAPI capture ─┘                                                        │
                                                                                              ▼
Claude Desktop / Claude Code ◄──── MCP stdio (get_transcript, add_marker, save_minutes...) ◄──┘
```

## Requisitos

- Windows 11 (ou Server 2022+), x64.
- .NET 10 SDK para compilar (`dotnet --version`). Para só executar o publicado, basta o runtime .NET 10.
- Microsoft Visual C++ Redistributable 2022 x64 (exigido pelo whisper.cpp).
- CPU com AVX2. Para GPU NVIDIA, veja "Desempenho".

## Compilar e publicar

```bash
dotnet publish -c Release -o publish
```

O executável fica em `publish\MeetingNotesMcp.exe`. O projeto tem um `nuget.config` próprio apontando
só para o nuget.org, para não depender de feeds privados configurados na máquina.

**Mantenha o projeto em um caminho curto** (ex.: `C:\Ferramentas\MeetingNotesMcp`). As DLLs nativas do
whisper.cpp ficam em `runtimes\win-x64\`; se o caminho completo passar de 260 caracteres, o Windows
recusa carregá-las (erro 206) e o whisper.cpp aborta com `GGML_ASSERT(prev != ggml_uncaught_exception)`.

## Testar sem MCP

```bash
publish\MeetingNotesMcp.exe devices
```

Lista as saídas (loopback) e entradas (microfones) ativas, marcando as padrão.

```bash
publish\MeetingNotesMcp.exe test 20
```

Grava 20 segundos, imprime as falas conforme são transcritas e grava os arquivos em
`Documentos\MeetingNotes\<data>_teste`. Na primeira execução o modelo Whisper é baixado para
`%LOCALAPPDATA%\MeetingNotesMcp\models`.

## Configuração (variáveis de ambiente)

| Variável | Padrão | Descrição |
|---|---|---|
| `MEETING_NOTES_MODEL` | `base` | `tiny`, `base`, `small`, `medium`, `large`, `large-turbo`. Para português, `small` é o melhor custo-benefício em CPU. |
| `MEETING_NOTES_LANGUAGE` | `pt` | Idioma do Whisper (`auto` detecta). |
| `MEETING_NOTES_OUTPUT` | `Documentos\MeetingNotes` | Pasta raiz; cada reunião ganha uma subpasta. Se a pasta Documentos estiver no OneDrive, as reuniões vão para lá; defina outra pasta se não quiser sincronizar. |
| `MEETING_NOTES_MODELS_DIR` | `%LOCALAPPDATA%\MeetingNotesMcp\models` | Onde ficam os modelos ggml. |
| `MEETING_NOTES_SPEAKER` | saída padrão | Trecho do nome do dispositivo de saída a capturar por loopback. |
| `MEETING_NOTES_MIC` | mic padrão de comunicação | Trecho do nome do microfone, ou `none` para não capturar o microfone. |
| `MEETING_NOTES_MY_NAME` | `Eu` | Rótulo das suas falas. |
| `MEETING_NOTES_OTHERS_NAME` | `Outros` | Rótulo das falas vindas do loopback. |
| `MEETING_NOTES_THREADS` | metade dos núcleos (2 a 8) | Threads do whisper.cpp. |
| `MEETING_NOTES_VAD_THRESHOLD` | `0.01` | Energia mínima (RMS) para considerar fala. Aumente se pegar ruído, diminua se perder falas baixas. |

## Conectar no Claude Desktop

1. Publique o projeto (acima) e anote o caminho completo de `MeetingNotesMcp.exe`.
2. Abra `%APPDATA%\Claude\claude_desktop_config.json` (no Claude Desktop: Configurações → Desenvolvedor → Editar configuração).
3. Adicione a entrada abaixo (há um exemplo completo em `claude_desktop_config.example.json`), ajustando o caminho e, se quiser, as variáveis:

```json
{
  "mcpServers": {
    "meeting-notes": {
      "command": "C:\\caminho\\para\\MeetingNotesMcp\\publish\\MeetingNotesMcp.exe",
      "args": ["serve"],
      "env": {
        "MEETING_NOTES_MODEL": "small",
        "MEETING_NOTES_MY_NAME": "Alisson",
        "MEETING_NOTES_OTHERS_NAME": "Participantes"
      }
    }
  }
}
```

4. Feche o Claude Desktop por completo (ícone na bandeja → Sair) e abra de novo. As ferramentas aparecem no menu de conectores da conversa.

Uso típico numa conversa:

- "Inicie a gravação da reunião *Planejamento da sprint 42*." → `start_meeting`
- "O que foi dito nos últimos minutos?" → `get_transcript` com `since_seq`
- "Registre a decisão: deploy na sexta." → `add_marker`
- "Encerre e gere a ata." → `stop_meeting`, depois `save_minutes`
- O prompt `gerar_ata` (menu de prompts do servidor) já traz o roteiro da ata.

## Conectar no Claude Code

Escopo de usuário (vale para qualquer pasta):

```bash
claude mcp add meeting-notes --scope user -e MEETING_NOTES_MODEL=small -e MEETING_NOTES_MY_NAME=Alisson -- "C:\caminho\para\MeetingNotesMcp\publish\MeetingNotesMcp.exe" serve
```

Ou por projeto: copie `.mcp.example.json` para `.mcp.json` na raiz do projeto e ajuste o caminho.
Confira com `claude mcp list` e `/mcp` dentro do Claude Code.

Para notas automáticas durante a reunião, use o loop do Claude Code:

```
/loop 5m Leia a transcrição nova com get_transcript (use o último seq que você já viu) e atualize a ata com save_minutes, mantendo as seções Resumo, Decisões, Ações e Pendências.
```

O servidor MCP vive enquanto a sessão do Claude Code (ou o Claude Desktop) estiver aberta. Se o cliente fechar no meio da reunião, o servidor encerra a gravação e grava o que já tem.

## Ferramentas MCP

| Ferramenta | O que faz |
|---|---|
| `start_meeting(title?)` | Inicia captura + transcrição. Cria a pasta da reunião. |
| `stop_meeting()` | Para, esvazia a fila de transcrição e grava `transcript.md`. |
| `get_status()` | Estado, duração, fila, dispositivos. |
| `get_transcript(since_seq, max_entries)` | Transcrição incremental: `[seq] HH:mm:ss Falante: texto`. |
| `add_marker(kind, text)` | Marca `decisao`, `acao`, `topico` ou `nota` com horário. |
| `save_minutes(markdown)` | Grava `ata.md` na pasta da reunião. |
| `list_meetings()` / `load_meeting(id)` | Reabre reuniões antigas. |
| `list_devices()` | Mesmo que o modo `devices`. |

Prompt: `gerar_ata`.

## Arquivos gerados por reunião

```
Documentos\MeetingNotes\2026-10-24_14-30_planejamento-da-sprint-42\
  meeting.json       metadados
  transcript.jsonl   uma entrada por linha, gravada em tempo real
  transcript.md      transcrição formatada (gerada no stop_meeting)
  ata.md             ata escrita pelo Claude via save_minutes
```

## Desempenho

- `base` em CPU moderna transcreve um trecho de 10 s em 1 a 3 s. `small` fica 2 a 3x mais lento, mas acerta muito mais em português. Se a fila (`segmentos_na_fila` no `get_status`) só cresce, desça de modelo ou aumente `MEETING_NOTES_THREADS`.
- GPU NVIDIA: adicione o pacote `Whisper.net.Runtime.Cuda` (CUDA 13) ou `Whisper.net.Runtime.Cuda12` ao `.csproj` e recompile. O runtime certo é escolhido sozinho; com GPU, `medium` roda em tempo real.
- CPU sem AVX: troque `Whisper.net.Runtime` por `Whisper.net.Runtime.NoAvx`.

## Limitações do protótipo

- Separação de falantes é só "você" (microfone) vs. "outros" (loopback). Diarização entre os outros participantes exige outro modelo (pyannote, ou serviços como Azure Speech, Deepgram, AssemblyAI).
- O loopback captura tudo que toca nas caixas, inclusive música ou notificações. A NAudio 3.x expõe `WasapiRecorderBuilder` com loopback por processo (só o Teams, por exemplo); é uma evolução natural.
- O detector de fala é por energia. Em ambientes ruidosos, o VAD Silero embutido no Whisper.net (`WhisperVadFactory`) é mais preciso.
- Whisper pode "inventar" texto em silêncio. Há filtros para os casos comuns, mas revise a ata.

## Aviso

Gravar reuniões exige informar os participantes. No Brasil, gravar conversa da qual você participa é legal,
mas a LGPD e as políticas da empresa pedem transparência. Avise no início da reunião.
