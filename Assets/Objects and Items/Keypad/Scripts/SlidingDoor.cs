using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace NavKeypad
{
    public class SlidingDoor : MonoBehaviour
    {
        [SerializeField] private Animator anim;
        public bool IsOpoen => isOpen;
        private bool isOpen = false;

        public void ToggleDoor()
        {
            isOpen = !isOpen;
            anim.SetBool("isOpen", isOpen);
        }

        public void OpenDoor()
        {
            Debug.Log($"OpenDoor called on {gameObject.name}");
            if (anim == null)
            {
                Debug.LogError("Animator is NULL on SlidingDoor!");
                return;
            }
            isOpen = true;
            anim.SetBool("isOpen", isOpen);
            Debug.Log("Animator isOpen set to true");
        }
        public void CloseDoor()
        {
            isOpen = false;
            anim.SetBool("isOpen", isOpen);
        }
    }
}