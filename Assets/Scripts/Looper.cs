using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class Looper : MonoBehaviour
{
    [SerializeField] AudioSource[] audioSources;
    [SerializeField] Image waveformImage;
    [SerializeField] GameObject feedbackLight;

    bool recording = false;
    double recStart;
    double recEnd;
    double clipRealDuration;

    AudioClip recordClip;
    Coroutine loopRoutine = null;

    [Header("PlayHead")]
    [SerializeField] Transform playHead;

    [Header("Settings - Looper")]
    [SerializeField] float looOffsetOnSliderChange = 0.75f;
    [SerializeField] float pressedDurationToReset = 2f;
    float pressedTime = 0;

    [Header("Wave Selector")]
    [SerializeField] Slider leftSlider;
    [SerializeField] Slider rightSlider;
    double clipAdjustedStartTime = 0;
    double clipAdjustedDuration;

    [Header("Settings - Waveformer")]
    public int width = 1024;
    public int height = 64;
    public Color background = Color.black;
    public Color foreground = Color.yellow;

    private int samplesize;
    private float[] samples = null;
    private float[] waveform = null;
    private float arrowoffsetx;

    private void Start()
    {
        int minFreq;
        int maxFreq;

        Screen.sleepTimeout = SleepTimeout.NeverSleep;

        for (int i = 0; i < Microphone.devices.Length; i++)
        {
            Microphone.GetDeviceCaps(Microphone.devices[i], out minFreq, out maxFreq);
            Debug.Log($"Device #{i} : {Microphone.devices[i]}");
            Debug.Log($"with minFreq={minFreq} / maxFreq={maxFreq}");
            Debug.Log("==========================================");
        }
    }

    // Button Event
    public void OnPedalPress()
    {
        if (!recording)
        {
            recording = true;
            recStart = AudioSettings.dspTime;

            if (loopRoutine != null) StopCoroutine(loopRoutine);
            audioSources[0].Stop();
            audioSources[1].Stop();

            feedbackLight.SetActive(true);
            recordClip = Microphone.Start(Microphone.devices[0], false, 60, 44100);
        }
        else
        {
            recording = false;
            recEnd = AudioSettings.dspTime;
            clipRealDuration = recEnd - recStart;

            feedbackLight.SetActive(false);
            Microphone.End(Microphone.devices[0]);

            waveformImage.sprite = GetWaveformSprite(recordClip);
            waveformImage.gameObject.SetActive(true);

            audioSources[0].clip = recordClip;
            audioSources[1].clip = recordClip;

            PlayLoop(recEnd);
        }
    }

    private void PlayLoop(double startDspTime, float offsetFactor = 0)
    {
        double loopDuration = clipAdjustedDuration > 0 ? clipAdjustedDuration : clipRealDuration;
        loopRoutine = StartCoroutine(LoopRecordingRoutine(startDspTime, loopDuration, Mathf.Clamp(offsetFactor, 0, 1)));
    }

    // Set firstOffset to around 70%-85% of the loop so user
    // can know much faster if the loop will complete correctly
    private IEnumerator LoopRecordingRoutine(double start, double loopDuration, float offsetFactor = 0)
    {
        double durationOffseted = clipAdjustedDuration * offsetFactor;

        while (audioSources[0].isPlaying)
        {
            SetPlayheadPosition(audioSources[0].time, (float)clipRealDuration);
            yield return null;
        }

        audioSources[0].time = (float)clipAdjustedStartTime + (float)durationOffseted;          //put head on correct start time
        audioSources[0].PlayScheduled(start);                                                   //schedule play
        audioSources[0].SetScheduledEndTime(start + 1 * loopDuration - durationOffseted);         //schedule end

        while (audioSources[1].isPlaying)
        {
            SetPlayheadPosition(audioSources[1].time, (float)clipRealDuration);
            yield return null;
        }

        audioSources[1].time = (float)clipAdjustedStartTime;                                    //put head on correct start time
        audioSources[1].PlayScheduled(start + 1 * loopDuration - durationOffseted);               //schedule play
        audioSources[1].SetScheduledEndTime(start + 2 * loopDuration - durationOffseted);         //schedule end

        loopRoutine = StartCoroutine(LoopRecordingRoutine(start + 2 * loopDuration - durationOffseted, loopDuration));
    }

    private void SetPlayheadPosition(float time, float duration)
    {
        playHead.localPosition = new Vector3(((time / duration) * width) - width / 2, 0, 0);
    }

    public void OnWaveSelectorSliderChange(bool isLeftSlider = true)
    {
        StopAllAudios();

        clipAdjustedStartTime = (double)leftSlider.normalizedValue * 0.5f * clipRealDuration;
        clipAdjustedDuration = clipRealDuration - clipAdjustedStartTime - (double)rightSlider.normalizedValue * 0.5f * clipRealDuration;

        PlayLoop(AudioSettings.dspTime + 0.5f, 0.85f);
    }

    private void StopAllAudios()
    {
        if (loopRoutine != null) StopCoroutine(loopRoutine);
        audioSources[0].Stop();
        audioSources[1].Stop();
    }

    private Sprite GetWaveformSprite(AudioClip clip)
    {
        int halfheight = height / 2;

        // get the sound data
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        waveform = new float[width];

        samplesize = clip.samples * clip.channels;
        samples = new float[samplesize];
        clip.GetData(samples, 0);

        int clipRealSamples = Mathf.CeilToInt((float)(clipRealDuration / clip.length) * clip.samples);
        int realSampleSize = clipRealSamples * clip.channels;
        float[] realSamples = new float[realSampleSize];

        var maxSampleValue = 0f;
        for (int i = 0; i < realSampleSize; i++)
        {
            realSamples[i] = samples[i];
            maxSampleValue = Mathf.Max(maxSampleValue, Mathf.Abs(realSamples[i]));
        }

        //int packsize = (samplesize / width);
        //for (int w = 0; w < width; w++)
        //{
        //    waveform[w] = Mathf.Abs(samples[w * packsize]);
        //}
        int packsize = (realSampleSize / width);
        for (int w = 0; w < width; w++)
        {
            waveform[w] = Mathf.Abs(realSamples[w * packsize]);
        }

        // map the sound data to texture
        // 1 - clear
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                tex.SetPixel(x, y, background);
            }
        }

        var heightScale = height / maxSampleValue;

        // 2 - plot
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < waveform[x] * heightScale; y++)
            {
                tex.SetPixel(x, halfheight + y, foreground);
                tex.SetPixel(x, halfheight - y, foreground);
            }
        }

        tex.Apply();

        Rect rect = new Rect(Vector2.zero, new Vector2(width, height));
        return Sprite.Create(tex, rect, Vector2.zero);
    }

    // Holding button for enough time will restart app
    public void OnPedalDown()
    {
        pressedTime = Time.time;
    }
    public void OnPedalUp()
    {
        pressedTime = Time.time - pressedTime;
        if (pressedTime > pressedDurationToReset) RestartLoopWhole();
    }

    public void RestartLoopWhole()
    {
        SceneManager.LoadScene(0);
    }
}
