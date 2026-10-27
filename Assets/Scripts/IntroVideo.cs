using UnityEngine;
using UnityEngine.Video;
using UnityEngine.SceneManagement;

public class IntroVideo : MonoBehaviour
{
    private VideoPlayer videoPlayer;

    void Start()
    {
        videoPlayer = GetComponent<VideoPlayer>();
        // Nos suscribimos al evento que detecta cuando el video termina
        videoPlayer.loopPointReached += AlTerminarVideo;
    }

    void AlTerminarVideo(VideoPlayer vp)
    {
        SceneManager.LoadScene("Menu");
    }
}
