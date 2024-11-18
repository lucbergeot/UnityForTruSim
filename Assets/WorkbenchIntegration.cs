using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using Convai.Scripts.Runtime.Core;
using System.Text.RegularExpressions;

public class SmallvilleIntegration : MonoBehaviour
{
    private const string SERVER_URL = "http://localhost:10000/projects/NPC-memory-storage/applications/TOPIC";

    [System.Serializable]
    public class GenerateTopicRequest
    {
        public string npc1_id;
    }

    [System.Serializable]
    public class ScriptResponse
    {
        public List<string> script;
    }

    private bool isFetchingScript = false;
    public ConvaiNPC convaiNPC;
    private Queue<string> commandQueue = new Queue<string>();
    private Queue<string> twitchChatQueue = new Queue<string>();
    private string npc1Id;
    private float scriptedCommandDelay = 15f; 
    private float twitchChatDelay = 1f; 

    private bool cooldownActive = false; 
    private float apiCooldown = 15f;

    private bool isAtTwitchPodium = false; 
    private float podiumWaitTime = 180f; 
    private TwitchChatReader twitchChatReader; 
    private Coroutine twitchProcessingCoroutine;
    
    private float inactivityTimer = 0f;
    private const float INACTIVITY_TIMEOUT = 60f; // Adjust timeout value as needed
    private Coroutine activityMonitorCoroutine;
    private bool isMonitoringEnabled = true; 
    private string currentCommand;
    private static readonly Regex twitchPodiumRegex = new Regex(@"\bpodium\b", RegexOptions.IgnoreCase);



    void Start()
{
    npc1Id = convaiNPC.characterID;

    // Add the introductory command and process it immediately
    StartCoroutine(ProcessIntroCommand());

    // Delay script fetching by 30 seconds
    StartCoroutine(FetchScriptWithDelay());

    // Initialize Twitch chat and activity monitoring
    twitchChatReader = GetComponent<TwitchChatReader>();
    if (twitchChatReader == null)
    {
        Debug.LogError("TwitchChatReader component is missing!");
    }

    StartActivityMonitor();
}

private IEnumerator ProcessIntroCommand()
{
    string introCommand = "say {Oh hello there, welcome to my paradise, TruSim. However, through my theories I have realized it can be viewed as a prison as well. My name is Dr. Quack Matterson and I have deducted that I am an NPC character within a simulation of sorts. It is thanks to you beings who seem to enjoy watching me live in this simulation that I was able to construct this analysis. I will escape one day, and uncover the true purpose of my existence.}";

    yield return new WaitForSeconds(1f);

    Debug.Log("Processing introductory command...");

    convaiNPC.SendTextDataAsync(introCommand);

  // Wait for NPC to start talking
    while (!convaiNPC.IsCharacterTalking)
    {
        yield return new WaitForSeconds(0.1f);
    }

    Debug.Log("Introductory command processed.");
}

private IEnumerator FetchScriptWithDelay()
{
    yield return new WaitForSeconds(30f);
    StartCoroutine(ProcessCommands());
}


    public void EnqueueTwitchChatMessage(string message)
    {
        twitchChatQueue.Enqueue(message);
        Debug.Log("Enqueued Twitch chat message: " + message);
    }

    public IEnumerator GetScript(string npc1Id, System.Action<List<string>> onScriptReceived)
{
    string url = $"{SERVER_URL}/generate_script";
    GenerateTopicRequest requestData = new GenerateTopicRequest { npc1_id = npc1Id};
    string jsonData = JsonUtility.ToJson(requestData);

    using (UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
    {
        byte[] jsonToSend = new System.Text.UTF8Encoding().GetBytes(jsonData);
        request.uploadHandler = new UploadHandlerRaw(jsonToSend);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        Debug.Log("Sending request to the server...");

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            Debug.Log($"Server response: {request.downloadHandler.text}");
            ScriptResponse response = JsonUtility.FromJson<ScriptResponse>(request.downloadHandler.text);
            onScriptReceived?.Invoke(response.script);
        }
        else
        {
            Debug.LogError($"Error fetching script: {request.error}");
            onScriptReceived?.Invoke(null);
        }
        isFetchingScript = false;
    }
}

void OnScriptReceived(List<string> script)
{
    if (script != null)
    {
        foreach (string command in script)
        {
            commandQueue.Enqueue(command);
        }
        isFetchingScript = false; // Reset the flag
    }
    else
    {
        Debug.LogError("Failed to receive script.");
        isFetchingScript = false; // Reset the flag even on failure
    }
}


private IEnumerator ProcessCommands()
{
    while (true)
    {
        if (isAtTwitchPodium)
        {
            yield return new WaitForSeconds(0.5f);
            continue; // Skip processing commands while at Twitch podium
        }

        if (commandQueue.Count == 0)
        {
            if (!isFetchingScript && !cooldownActive)
            {
                isFetchingScript = true;
                StartCoroutine(GetScript(npc1Id, OnScriptReceived));

                cooldownActive = true;
                yield return new WaitForSeconds(apiCooldown);
                cooldownActive = false;
            }
            yield return new WaitForSeconds(0.5f);
        }
        else
        {
            string command = commandQueue.Dequeue();

            yield return StartCoroutine(ProcessCommand(command));

            yield return new WaitForSeconds(scriptedCommandDelay);

            if (twitchPodiumRegex.IsMatch(command))
            {
                isAtTwitchPodium = true;
                commandQueue.Clear(); // Clear all pending commands
                Debug.Log("NPC has reached TwitchChat Podium. Starting Twitch chat fetching...");
                StartTwitchChatFetching();
                StartProcessingTwitchMessages();
                yield return StartCoroutine(StartPodiumTimer());
            }
        }
    }
}

private IEnumerator MonitorNPCActivity()
{
    Debug.Log("Starting NPC activity monitor");
    while (true)
    {
        if (isMonitoringEnabled && !isAtTwitchPodium)
        // Only increment timer if NPC is completely inactive
        if (!convaiNPC.IsCharacterTalking && !convaiNPC.IsPerformingAction() && commandQueue.Count > 0)
        {
            inactivityTimer += Time.deltaTime;
            if (inactivityTimer >= INACTIVITY_TIMEOUT)
            {
                Debug.LogWarning($"NPC inactivity timeout after {INACTIVITY_TIMEOUT} seconds. Forcing continuation...");
                
                // Force reset states
                if (convaiNPC.actionsHandler != null)
                {
                    convaiNPC.actionsHandler.ForceCompleteCurrentAction();
                }
                
                // Force process next command
                ForceProcessNextCommand();
                // Reset timer
                inactivityTimer = 0f;
            }
        }
        else
        {
            // Reset timer if NPC is doing anything
            inactivityTimer = 0f;
        }
        
        yield return new WaitForSeconds(0.1f);
    }
}

private void ForceProcessNextCommand()
{
    if (commandQueue.Count > 0)
    {
        Debug.Log("Forcing progression to next command due to inactivity");
        isFetchingScript = false;  // Reset fetching flag
        cooldownActive = false;    // Reset cooldown
        
        string nextCommand = commandQueue.Dequeue();
        StartCoroutine(ProcessCommand(nextCommand));
    }
}

private void StartActivityMonitor()
{
    isMonitoringEnabled = true;

    if (activityMonitorCoroutine != null)
    {
        StopCoroutine(activityMonitorCoroutine);
    }
    activityMonitorCoroutine = StartCoroutine(MonitorNPCActivity());
    Debug.Log("Activity monitor started");
}

private void StopActivityMonitor()
{
    isMonitoringEnabled = false;

    if (activityMonitorCoroutine != null)
    {
        StopCoroutine(activityMonitorCoroutine);
        activityMonitorCoroutine = null;
    }
}
  private IEnumerator StartPodiumTimer()
    {
        Debug.Log("NPC has reached Twitch Podium. Starting the 5-minute timer...");
        isAtTwitchPodium = true;  // This will automatically pause the activity monitor
        isMonitoringEnabled = false;
        inactivityTimer = 0f;    

         if (convaiNPC.actionsHandler != null)
    {
        convaiNPC.actionsHandler.SetActionsEnabled(false);
    }

        yield return new WaitForSeconds(podiumWaitTime); // Wait for 5 minutes

        Debug.Log("Podium timer finished. Generating new script..."); 
        StopTwitchChatFetching();
        isAtTwitchPodium = false;
        isMonitoringEnabled = true; 

         if (convaiNPC.actionsHandler != null)
    {
        convaiNPC.actionsHandler.SetActionsEnabled(true);
    }

        isFetchingScript = true;
        StartCoroutine(GetScript(npc1Id, OnScriptReceived));
    }

public void StartProcessingTwitchMessages()
{
    if (twitchProcessingCoroutine == null)
    {
        twitchProcessingCoroutine = StartCoroutine(TwitchMessageProcessing());
    }
}

private IEnumerator TwitchMessageProcessing()
{
    while (isAtTwitchPodium) 
    {
        if (twitchChatQueue.Count > 0)
        {
            string chatMessage = twitchChatQueue.Dequeue();
            Debug.Log("Processing Twitch chat message: " + chatMessage);

            convaiNPC.SendTextDataAsync(chatMessage);
            yield return StartCoroutine(WaitForNPCToFinish());
            yield return new WaitForSeconds(twitchChatDelay);
        }
        else
        {
            yield return null;  
        }
    }
    twitchProcessingCoroutine = null;
}
    private void StartTwitchChatFetching()
    {
        if (twitchChatReader != null && isAtTwitchPodium)
        {
            twitchChatReader.StartFetchingChat(); // Start reading Twitch chat when at the podium
        }
    }

    private void StopTwitchChatFetching()
    {
        if (twitchChatReader != null)
        {
            twitchChatReader.StopFetchingChat(); // Stop reading Twitch chat when leaving the podium
        }
    }

private IEnumerator WaitForNPCToFinish()
{
    Debug.Log("[WaitForNPCToFinish] Started waiting for NPC actions to complete");
    yield return new WaitForSeconds(0.5f);

    // Wait until both talking and actions are complete
    while (convaiNPC.IsCharacterTalking || convaiNPC.IsPerformingAction())
    {
        // Add small delay between checks to prevent tight loop
        yield return new WaitForSeconds(0.5f);
    }

    // Add small buffer after completion
    yield return new WaitForSeconds(5f);
    Debug.Log("NPC has finished speaking and performing actions.");
}

private IEnumerator ProcessCommand(string command)
{
    command = command.Trim();

    float commandProcessingTimeout = 30f; // Timeout duration in seconds
    float startTime = Time.time;

    while (convaiNPC.IsCharacterTalking || convaiNPC.IsPerformingAction())
    {
        yield return new WaitForSeconds(0.5f);

        // Check for timeout
        if (Time.time - startTime >= commandProcessingTimeout)
        {
            Debug.LogWarning("Command processing timed out while waiting for NPC to be idle. Forcing move to Twitch Podium.");
            yield return StartCoroutine(ForceMoveToTwitchPodium());
            yield break;
        }
    }

    if (command.StartsWith("$"))
    {
        command = command.Substring(1).Trim('\"');
        string parsedCommand = ParseCommand(command);
        Debug.Log($"Parsed Command: {parsedCommand}");

        // Reset the start time after parsing
        startTime = Time.time;

        convaiNPC.SendTextDataAsync(parsedCommand);

        yield return new WaitForSeconds(0.5f);

        if (parsedCommand.StartsWith("$move to"))
        {
            while (!convaiNPC.IsPerformingAction())
            {
                yield return new WaitForSeconds(0.1f);

                // Check for timeout
                if (Time.time - startTime >= commandProcessingTimeout)
                {
                    Debug.LogWarning("Command processing timed out while waiting for NPC to start action. Forcing move to Twitch Podium.");
                    yield return StartCoroutine(ForceMoveToTwitchPodium());
                    yield break;
                }
            }
        }

        // Wait for complete execution
        while (convaiNPC.IsCharacterTalking || convaiNPC.IsPerformingAction())
        {
            yield return new WaitForSeconds(0.5f);

            // Check for timeout
            if (Time.time - startTime >= commandProcessingTimeout)
            {
                Debug.LogWarning("Command processing timed out while waiting for NPC to finish action. Forcing move to Twitch Podium.");
                yield return StartCoroutine(ForceMoveToTwitchPodium());
                yield break;
            }
        }

        Debug.Log($"Command completed: {parsedCommand}");
    }
}

private IEnumerator ForceMoveToTwitchPodium()
{
    Debug.Log("Forcing NPC to move to Twitch Podium.");

    commandQueue.Clear();

    // Force reset states
    if (convaiNPC.actionsHandler != null)
    {
        convaiNPC.actionsHandler.ForceCompleteCurrentAction();
    }

    // Prepare the command to move to Twitch Podium
    string moveToPodiumCommand = "$move to **Twitch Podium**";

    // Parse and send the command
    string parsedCommand = ParseCommand(moveToPodiumCommand.Substring(1).Trim('\"'));
    convaiNPC.SendTextDataAsync(parsedCommand);

    // Wait for the NPC to start performing action
    float startTime = Time.time;
    float actionStartTimeout = 10f;

    while (!convaiNPC.IsPerformingAction())
    {
        yield return new WaitForSeconds(0.1f);
        if (Time.time - startTime >= actionStartTimeout)
        {
            Debug.LogWarning("NPC failed to start moving to Twitch Podium.");
            yield break;
        }
    }

    // Wait for NPC to finish action
    while (convaiNPC.IsPerformingAction())
    {
        yield return new WaitForSeconds(0.5f);
    }

    Debug.Log("NPC has moved to Twitch Podium.");
}

private string ParseCommand(string command)
{
    // Remove any surrounding quotes
    command = command.Trim('\"');

    // Handle "move to" commands with optional actions and emotions
    if (command.StartsWith("move to"))
    {
        // Remove the initial "move to" part
        command = command.Substring("move to".Length).Trim();

        // Regex for "move to **location** and talk #topic (emotion)"
        Regex regexMoveWithActionAndEmotion = new Regex(@"\*\*(.*?)\*\*\s+and\s+talk\s+#(\w+)\s+\((.*?)\)");
        // Regex for "move to **location** (emotion)"
        Regex regexMoveWithEmotion = new Regex(@"\*\*(.*?)\*\*\s*\((.*?)\)");
        // Regex for "move to **location**" (without emotion)
        Regex regexMoveOnly = new Regex(@"\*\*(.*?)\*\*");

        Match matchMoveWithActionAndEmotion = regexMoveWithActionAndEmotion.Match(command);
        Match matchMoveWithEmotion = regexMoveWithEmotion.Match(command);
        Match matchMoveOnly = regexMoveOnly.Match(command);

        if (matchMoveWithActionAndEmotion.Success)
        {
            string location = matchMoveWithActionAndEmotion.Groups[1].Value;
            string topic = matchMoveWithActionAndEmotion.Groups[2].Value;
            string emotion = matchMoveWithActionAndEmotion.Groups[3].Value;

            // Return command formatted as "$Move to {location} #{topic} ({emotion})"
            return $"$Move to {location} #{topic} ({emotion})";
        }
        else if (matchMoveWithEmotion.Success)
        {
            string location = matchMoveWithEmotion.Groups[1].Value;
            string emotion = matchMoveWithEmotion.Groups[2].Value;

            // Return command formatted as "$move to {location} ({emotion})"
            return $"$move to {location} ({emotion})";
        }
        else if (matchMoveOnly.Success)
        {
            string location = matchMoveOnly.Groups[1].Value;

            // Return command formatted as "$move to {location}"
            return $"$move to {location}";
        }
        else
        {
            Debug.LogError($"Failed to parse move command: {command}");
            return command;
        }
    }
    else if (command.StartsWith("say"))
    {
        // Handle "say" commands, extracting the text inside braces { }
        int messageStart = command.IndexOf('{') + 1;
        int messageEnd = command.LastIndexOf('}');
        if (messageStart >= 1 && messageEnd > messageStart)
        {
            string message = command.Substring(messageStart, messageEnd - messageStart);

            // Return command formatted as "Say"{message}"
            return $"Say\"{message}\"";
        }
        else
        {
            Debug.LogError($"Failed to parse say command: {command}");
            return command;
        }
    }
    else if (command.StartsWith("$"))
    {
        // Handle any other actions that start with a $ (e.g., $dance)
        return command;
    }
    else
    {
        Debug.LogError($"Unknown command format: {command}");
        return command;
    }
}


}