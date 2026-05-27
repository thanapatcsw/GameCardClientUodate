using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class CanvasSparkleSpawner : MonoBehaviour
{
    [Header("Spawner Settings")]
    [Tooltip("Sprite to use for sparkles. If empty, a simple procedurally generated soft dot will be used.")]
    public Sprite sparkleSprite;
    
    [Tooltip("Maximum number of active sparkles at once.")]
    public int maxSparkles = 50;
    
    [Tooltip("How many sparkles to spawn per second.")]
    public float spawnRate = 5f;
    
    [Header("Sparkle Properties")]
    public Color minColor = new Color(1f, 1f, 1f, 0.2f);
    public Color maxColor = new Color(1f, 1f, 1f, 0.8f);
    
    public float minSize = 5f;
    public float maxSize = 25f;
    
    public float minLifetime = 2f;
    public float maxLifetime = 6f;
    
    public float minSpeedY = 10f;
    public float maxSpeedY = 40f;
    
    [Header("Movement details")]
    [Tooltip("Amount of horizontal drifting (sway) using a sine wave.")]
    public float minSway = 10f;
    public float maxSway = 30f;
    public float swaySpeed = 1f;

    [Header("Pre-Warm Settings")]
    [Tooltip("If enabled, scatters sparkles all over the screen instantly when the game starts.")]
    public bool preWarm = true;
    
    private float spawnTimer = 0f;
    private List<SparkleInstance> activeSparkles = new List<SparkleInstance>();
    private Sprite defaultWhiteDotSprite;
    private RectTransform rectTransform;

    private class SparkleInstance
    {
        public GameObject gameObject;
        public RectTransform rectTransform;
        public Image image;
        public Vector2 startPosition;
        public float speedY;
        public float swayAmount;
        public float swayOffset;
        public float lifetime;
        public float maxLifetime;
        public float targetScale;
        public Color baseColor;
    }

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        if (rectTransform == null)
        {
            rectTransform = gameObject.AddComponent<RectTransform>();
        }

        // Auto-stretch RectTransform if it's currently at default size (0, 0)
        if (rectTransform.anchorMin == new Vector2(0.5f, 0.5f) && rectTransform.anchorMax == new Vector2(0.5f, 0.5f))
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
            GameLog.Log("[CanvasSparkleSpawner] Automatically stretched RectTransform anchors to Full Screen.");
        }
        
        // If no sprite is selected, generate a soft glow dot texture programmatically
        if (sparkleSprite == null)
        {
            GenerateDefaultGlowSprite();
        }

        GameLog.Log("[CanvasSparkleSpawner] Initialized successfully.");
    }

    private void Start()
    {
        if (preWarm)
        {
            // Pre-spawn particles all over the screen height
            int initialCount = Mathf.RoundToInt(maxSparkles * 0.8f);
            for (int i = 0; i < initialCount; i++)
            {
                SpawnSparkle(preWarmed: true);
            }
            GameLog.Log($"[CanvasSparkleSpawner] Pre-warmed screen with {initialCount} sparkles scattered across the screen.");
        }
    }

    private void Update()
    {
        // Initial spawning up to maxSparkles
        if (activeSparkles.Count < maxSparkles)
        {
            spawnTimer += Time.deltaTime;
            float timeBetweenSpawns = 1f / spawnRate;
            if (spawnTimer >= timeBetweenSpawns)
            {
                SpawnSparkle(preWarmed: false);
                spawnTimer = 0f;
            }
        }

        // Update active sparkles (with wrap-around / pooling logic)
        for (int i = 0; i < activeSparkles.Count; i++)
        {
            UpdateSparkle(activeSparkles[i]);
        }
    }

    private void SpawnSparkle(bool preWarmed)
    {
        GameObject go = new GameObject("Sparkle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(transform, false);

        RectTransform sparkleRect = go.GetComponent<RectTransform>();
        Image sparkleImage = go.GetComponent<Image>();

        // Select sprite
        sparkleImage.sprite = sparkleSprite != null ? sparkleSprite : defaultWhiteDotSprite;
        sparkleImage.raycastTarget = false;

        // Position - Spawn randomly along the bottom boundary, or all over screen if prewarmed
        float width = rectTransform.rect.width;
        float height = rectTransform.rect.height;

        // Fallback if spawner size is zero or not updated yet
        if (width <= 0f) width = Screen.width;
        if (height <= 0f) height = Screen.height;

        float randomX = Random.Range(-width / 2f, width / 2f);
        float spawnY;
        float initialLifetime = 0f;

        if (preWarmed)
        {
            // Distribute Y anywhere from bottom to top
            spawnY = Random.Range(-height / 2f, height / 2f);
            
            // Set a random initial lifetime progress so they fade at different times
            float maxLife = Random.Range(minLifetime, maxLifetime);
            initialLifetime = Random.Range(0f, maxLife * 0.9f);
        }
        else
        {
            spawnY = -height / 2f - 20f; // Spawn below screen
        }

        sparkleRect.anchoredPosition = new Vector2(randomX, spawnY);
        
        // Randomize characteristics
        SparkleInstance instance = new SparkleInstance
        {
            gameObject = go,
            rectTransform = sparkleRect,
            image = sparkleImage,
            startPosition = sparkleRect.anchoredPosition,
            speedY = Random.Range(minSpeedY, maxSpeedY),
            swayAmount = Random.Range(minSway, maxSway),
            swayOffset = Random.Range(0f, 360f),
            lifetime = initialLifetime,
            maxLifetime = Random.Range(minLifetime, maxLifetime),
            targetScale = 1f,
            baseColor = Color.Lerp(minColor, maxColor, Random.value)
        };

        // Scale
        float size = Random.Range(minSize, maxSize);
        sparkleRect.sizeDelta = new Vector2(size, size);

        // Add to tracking list
        activeSparkles.Add(instance);
    }

    private void UpdateSparkle(SparkleInstance sparkle)
    {
        sparkle.lifetime += Time.deltaTime;

        // Move upward
        Vector2 pos = sparkle.rectTransform.anchoredPosition;
        pos.y += sparkle.speedY * Time.deltaTime;

        // Sway side to side (Sine wave horizontal drift)
        float sway = Mathf.Sin((sparkle.lifetime * swaySpeed) + sparkle.swayOffset) * sparkle.swayAmount * Time.deltaTime;
        pos.x += sway;
        
        sparkle.rectTransform.anchoredPosition = pos;

        // Wrap-Around / Reset Particle if it exceeds lifetime
        if (sparkle.lifetime >= sparkle.maxLifetime)
        {
            ResetSparkle(sparkle);
            return;
        }

        float progress = sparkle.lifetime / sparkle.maxLifetime;

        // Smooth scale: Scale up initially, then scale down near the end of lifetime
        float currentScale = 1f;
        if (progress < 0.2f)
        {
            currentScale = progress / 0.2f; // Fade in scale
        }
        else if (progress > 0.8f)
        {
            currentScale = (1f - progress) / 0.2f; // Fade out scale
        }
        sparkle.rectTransform.localScale = new Vector3(currentScale, currentScale, 1f);

        // Fade transparency in and out over lifetime
        float alpha = 1f;
        if (progress < 0.2f)
        {
            alpha = progress / 0.2f;
        }
        else if (progress > 0.7f)
        {
            alpha = (1f - progress) / 0.3f;
        }

        Color finalColor = sparkle.baseColor;
        finalColor.a *= alpha;
        sparkle.image.color = finalColor;
    }

    private void ResetSparkle(SparkleInstance sparkle)
    {
        float width = rectTransform.rect.width;
        float height = rectTransform.rect.height;

        if (width <= 0f) width = Screen.width;
        if (height <= 0f) height = Screen.height;

        // Teleport back to bottom of screen
        float randomX = Random.Range(-width / 2f, width / 2f);
        float spawnY = -height / 2f - 20f;
        
        sparkle.rectTransform.anchoredPosition = new Vector2(randomX, spawnY);
        
        // Re-randomize properties so it acts like a completely new particle
        sparkle.speedY = Random.Range(minSpeedY, maxSpeedY);
        sparkle.swayAmount = Random.Range(minSway, maxSway);
        sparkle.swayOffset = Random.Range(0f, 360f);
        sparkle.lifetime = 0f;
        sparkle.maxLifetime = Random.Range(minLifetime, maxLifetime);
        sparkle.baseColor = Color.Lerp(minColor, maxColor, Random.value);
        
        float size = Random.Range(minSize, maxSize);
        sparkle.rectTransform.sizeDelta = new Vector2(size, size);
        sparkle.rectTransform.localScale = Vector3.zero;
    }

    private void GenerateDefaultGlowSprite()
    {
        int texSize = 64;
        Texture2D texture = new Texture2D(texSize, texSize, TextureFormat.RGBA32, false);
        
        // Procedurally generate a soft radial glow
        Vector2 center = new Vector2(texSize / 2f, texSize / 2f);
        float maxRadius = texSize / 2f;

        for (int y = 0; y < texSize; y++)
        {
            for (int x = 0; x < texSize; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center);
                float ratio = distance / maxRadius;
                
                // Falloff algorithm for a soft organic glow
                float alpha = Mathf.Max(0f, 1f - ratio);
                alpha = Mathf.Pow(alpha, 2.5f); // Smooth out the edges

                Color color = new Color(1f, 1f, 1f, alpha);
                texture.SetPixel(x, y, color);
            }
        }
        
        texture.Apply();
        
        // Create Sprite
        defaultWhiteDotSprite = Sprite.Create(texture, new Rect(0, 0, texSize, texSize), new Vector2(0.5f, 0.5f));
    }

    private void OnDestroy()
    {
        foreach (var sparkle in activeSparkles)
        {
            if (sparkle.gameObject != null)
            {
                Destroy(sparkle.gameObject);
            }
        }
        activeSparkles.Clear();
    }
}
