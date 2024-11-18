using UnityEngine;
using UnityEngine.Networking;
using System.Collections;

public class NPCInfoUpdater : MonoBehaviour
{
    public static NPCInfoUpdater Instance { get; private set; }

    private void Awake()
    {
        // Ensure there's only one instance of NPCInfoUpdater
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        // Optional: Make the instance persistent across scenes
        DontDestroyOnLoad(gameObject);
    }
    // Base URL of the server
    private const string SERVER_URL = "http://localhost:10000/projects/NPC-memory-storage/applications/TOPIC";

    // Serializable response structure to match the JSON from `/get_inner_thoughts`
    [System.Serializable]
    public class InnerThoughtsResponse
    {
        public string line1; // Example: "Username: message"
        public string line2; // Example: "Interactions: 10, Sentiment Score: 0.85"
        public string line3; // Example: "Status: friend"
        public string line4; // Example: "Inner Thought: some thought text"
        public string line5; // Example: "Current Emotional State: Joy: 50%, Sadness: 30%"
    }

    // Public references to UI elements
    public TMPro.TextMeshProUGUI TextUser;
    public TMPro.TextMeshProUGUI TextInnerThought;
    public TMPro.TextMeshProUGUI TextTotalInteractions;
    public TMPro.TextMeshProUGUI TextRelationshipStatus;
    public TMPro.TextMeshProUGUI TextEmotionalState;

    private bool isFetchingData = false; // To prevent multiple simultaneous calls

    void Start()
    {
        StartCoroutine(FetchDataPeriodically());
    }

    private IEnumerator FetchDataPeriodically()
    {
        while (true)
        {
            if (!isFetchingData)
            {
                isFetchingData = true;
                yield return StartCoroutine(GetInnerThoughts(OnDataReceived));
                isFetchingData = false;
            }

            // Wait for 5 seconds before fetching data again
            yield return new WaitForSeconds(5f);
        }
    }

    private IEnumerator GetInnerThoughts(System.Action<InnerThoughtsResponse> onResponseReceived)
    {
        string url = $"{SERVER_URL}/get_inner_thoughts";

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            Debug.Log("Sending request to fetch inner thoughts...");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log($"Response received: {request.downloadHandler.text}");

                // Parse the JSON response
                InnerThoughtsResponse response = JsonUtility.FromJson<InnerThoughtsResponse>(request.downloadHandler.text);

                // Pass the parsed data to the callback
                onResponseReceived?.Invoke(response);
            }
            else
            {
                Debug.LogError($"Error fetching inner thoughts: {request.error}");
                onResponseReceived?.Invoke(null);
            }
        }
    }

    private void OnDataReceived(InnerThoughtsResponse response)
{
    if (response != null)
    {
        string sentimentScore = ""; // We'll store this for use in the relationship status

        // Update Last Twitch Interaction if line1 is not empty
        if (!string.IsNullOrEmpty(response.line1))
        {
            // Parse line1 (username and message)
            string[] userParts = response.line1.Split(':');
            string username = userParts.Length > 0 ? userParts[0].Trim() : "Unknown User";
            string message = userParts.Length > 1 ? userParts[1].Trim() : "";
            TextUser.text = $"<b>Last Twitch Interaction:</b> {username} - {message}";
        }

        // Update Total Interactions and Sentiment Score if line2 is not empty
        if (!string.IsNullOrEmpty(response.line2))
        {
            // Parse line2 (interactions and sentiment score)
            string[] interactionParts = response.line2.Split(',');
            string totalInteractions = interactionParts.Length > 0
                ? interactionParts[0].Replace("Interactions:", "").Trim()
                : "0";
            sentimentScore = interactionParts.Length > 1
                ? interactionParts[1].Replace("Sentiment Score:", "").Trim()
                : "0.00";

            // Format the sentiment score to 3 digits with sign
            if (float.TryParse(sentimentScore, out float parsedScore))
            {
                sentimentScore = $"{parsedScore:+0.00;-0.00}";
            }

            TextTotalInteractions.text = $"<b>Total Interactions:</b> {totalInteractions}";
        }

        // Update Relationship Status if line3 is not empty
        if (!string.IsNullOrEmpty(response.line3))
        {
            // Parse line3 (status)
            string relationshipStatus = response.line3.Replace("Status:", "").Trim();
            string combinedRelationship = $"<b>Relationship Status:</b> {relationshipStatus} ({sentimentScore})";
            TextRelationshipStatus.text = combinedRelationship;
        }

        // Update Inner Thought if line4 is not empty
        if (!string.IsNullOrEmpty(response.line4))
        {
            // Parse line4 (inner thought)
            string innerThought = response.line4.Replace("Inner Thought:", "").Trim();
            TextInnerThought.text = $"<b>Inner Thought:</b> {innerThought}";
        }

        // Update Emotional State if line5 is not empty
        if (!string.IsNullOrEmpty(response.line5))
        {
            // Parse line5 (emotional state)
            string emotionalState = response.line5.Replace("Current Emotional State:", "").Trim();
            TextEmotionalState.text = $"<b>Emotional State:</b> {emotionalState}";
        }
    }
    else
    {
        Debug.LogWarning("Failed to fetch data or invalid response received.");
    }
}

}
