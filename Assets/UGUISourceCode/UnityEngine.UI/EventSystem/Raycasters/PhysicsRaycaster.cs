using System.Collections.Generic;
using UnityEngine.UI;

namespace UnityEngine.EventSystems
{
    /// <summary>
    /// Simple event system using physics raycasts.
    /// </summary>
    [AddComponentMenu("Event/Physics Raycaster")]
    [RequireComponent(typeof(Camera))]
    /// <summary>
    /// Raycaster for casting against 3D Physics components.
    /// </summary>
    public class PhysicsRaycaster : BaseRaycaster
    {
        /// <summary>
        /// Const to use for clarity when no event mask is set
        /// </summary>
        protected const int kNoEventMaskSet = -1;

        protected Camera m_EventCamera;

        /// <summary>
        /// Layer mask used to filter events. Always combined with the camera's culling mask if a camera is used.
        /// </summary>
        [SerializeField]
        protected LayerMask m_EventMask = kNoEventMaskSet;

        /// <summary>
        /// The max number of intersections allowed. 0 = allocating version anything else is non alloc.
        /// </summary>
        [SerializeField]
        protected int m_MaxRayIntersections = 0;
        protected int m_LastMaxRayIntersections = 0;

        RaycastHit[] m_Hits;

        protected PhysicsRaycaster()
        {}

        public override Camera eventCamera
        {
            get
            {
                if (m_EventCamera == null)
                    m_EventCamera = GetComponent<Camera>();
                return m_EventCamera ?? Camera.main;
            }
        }


        /// <summary>
        /// Depth used to determine the order of event processing.
        /// </summary>
        public virtual int depth
        {
            get { return (eventCamera != null) ? (int)eventCamera.depth : 0xFFFFFF; }
        }

        /// <summary>
        /// Event mask used to determine which objects will receive events.
        /// </summary>
        public int finalEventMask
        {
            get { return (eventCamera != null) ? eventCamera.cullingMask & m_EventMask : kNoEventMaskSet; }
        }

        /// <summary>
        /// Layer mask used to filter events. Always combined with the camera's culling mask if a camera is used.
        /// </summary>
        public LayerMask eventMask
        {
            get { return m_EventMask; }
            set { m_EventMask = value; }
        }

        /// <summary>
        /// Max number of ray intersection allowed to be found.
        /// </summary>
        /// <remarks>
        /// A value of zero will represent using the allocating version of the raycast function where as any other value will use the non allocating version.
        /// </remarks>
        public int maxRayIntersections
        {
            get { return m_MaxRayIntersections; }
            set { m_MaxRayIntersections = value; }
        }

        /// <summary>
        /// Returns a ray going from camera through the event position and the distance between the near and far clipping planes along that ray.
        /// </summary>
        /// <param name="eventData">The pointer event for which we will cast a ray.</param>
        /// <param name="ray">The ray to use.</param>
        /// <param name="distanceToClipPlane">The distance between the near and far clipping planes along the ray.</param>
        /// <returns>True if the operation was successful. false if it was not possible to compute, such as the eventPosition being outside of the view.</returns>
        protected bool ComputeRayAndDistance(PointerEventData eventData, ref Ray ray, ref float distanceToClipPlane)
        {
            if (eventCamera == null)
                return false;

            var eventPosition = Display.RelativeMouseAt(eventData.position);
            if (eventPosition != Vector3.zero)
            {
                // We support multiple display and display identification based on event position.
                int eventDisplayIndex = (int)eventPosition.z;

                // Discard events that are not part of this display so the user does not interact with multiple displays at once.
                if (eventDisplayIndex != eventCamera.targetDisplay)
                    return false;
            }
            else
            {
                // The multiple display system is not supported on all platforms, when it is not supported the returned position
                // will be all zeros so when the returned index is 0 we will default to the event data to be safe.
                eventPosition = eventData.position;
            }

            // Cull ray casts that are outside of the view rect. (case 636595)
            if (!eventCamera.pixelRect.Contains(eventPosition))
                return false;

            ray = eventCamera.ScreenPointToRay(eventPosition);
            // compensate far plane distance - see MouseEvents.cs
            float projectionDirection = ray.direction.z;
            distanceToClipPlane = Mathf.Approximately(0.0f, projectionDirection)
                ? Mathf.Infinity
                : Mathf.Abs((eventCamera.farClipPlane - eventCamera.nearClipPlane) / projectionDirection);
            return true;
        }

        //用于检测3D物理元素，并且保存被射线检测到物体的数据
        public override void Raycast(PointerEventData eventData, List<RaycastResult> resultAppendList)
        {
            Ray ray = new Ray();
            float distanceToClipPlane = 0;
            //判断是否超出摄像机的远近裁剪平面的距离
            if (!ComputeRayAndDistance(eventData, ref ray, ref distanceToClipPlane))
                return;

            int hitCount = 0;

            //采用ReflectionMethodsCache.Singleton.raycast3DAll()来获取所有射线照射到的对象
            //用反射的方式把Physics.RaycastAll()方法缓存下来，让Unity的Physics模块与UI模块，保持低耦合，没有过分依赖。
            if (m_MaxRayIntersections == 0)
            {
                if (ReflectionMethodsCache.Singleton.raycast3DAll == null)
                    return;

                m_Hits = ReflectionMethodsCache.Singleton.raycast3DAll(ray, distanceToClipPlane, finalEventMask);
                hitCount = m_Hits.Length;
            }
            else
            {
                if (ReflectionMethodsCache.Singleton.getRaycastNonAlloc == null)
                    return;

                if (m_LastMaxRayIntersections != m_MaxRayIntersections)
                {
                    m_Hits = new RaycastHit[m_MaxRayIntersections];
                    m_LastMaxRayIntersections = m_MaxRayIntersections;
                }

                hitCount = ReflectionMethodsCache.Singleton.getRaycastNonAlloc(ray, m_Hits, distanceToClipPlane, finalEventMask);
            }

            if (hitCount > 1)
                System.Array.Sort(m_Hits, (r1, r2) => r1.distance.CompareTo(r2.distance));
            //获取到被射线照射到的对象，根据距离进行排序，然后包装成RaycastResult,加入到resultAppendList中
            if (hitCount != 0)
            {
                for (int b = 0, bmax = hitCount; b < bmax; ++b)
                {
                    var result = new RaycastResult
                    {
                        gameObject = m_Hits[b].collider.gameObject,
                        module = this,
                        distance = m_Hits[b].distance,
                        worldPosition = m_Hits[b].point,
                        worldNormal = m_Hits[b].normal,
                        screenPosition = eventData.position,
                        index = resultAppendList.Count,
                        sortingLayer = 0,
                        sortingOrder = 0
                    };
                    resultAppendList.Add(result);
                }
            }
        }
    }
}
