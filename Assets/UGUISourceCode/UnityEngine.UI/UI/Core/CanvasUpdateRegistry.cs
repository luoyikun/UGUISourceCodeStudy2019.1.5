using System;
using System.Collections.Generic;
using UnityEngine.UI.Collections;

namespace UnityEngine.UI
{
    /// <summary>
    /// Values of 'update' called on a Canvas update.
    /// </summary>
    public enum CanvasUpdate
    {
        /// <summary>
        /// Called before layout.
        /// </summary>
        /// 布局重建前
        Prelayout = 0,
        /// <summary>
        /// Called for layout.
        /// </summary>
        /// 布局重建
        Layout = 1,
        /// <summary>
        /// Called after layout.
        /// </summary>
        /// 布局重建后
        PostLayout = 2,
        /// <summary>
        /// Called before rendering.
        /// </summary>
        /// 渲染前(图形重建前)
        PreRender = 3,
        /// <summary>
        /// Called late, before render.
        /// </summary>
        /// PreRender后，渲染前
        LatePreRender = 4,
        /// <summary>
        /// Max enum value. Always last.
        /// </summary>
        /// 最大枚举值
        MaxUpdateValue = 5
    }

    /// <summary>
    /// This is an element that can live on a Canvas.
    /// </summary>
    public interface ICanvasElement
    {
        /// <summary>
        /// Rebuild the element for the given stage.
        /// </summary>
        /// <param name="executing">The current CanvasUpdate stage being rebuild.</param>
        ///  // 根据CanvasUpdate的不同阶段重建元素
        void Rebuild(CanvasUpdate executing);

        /// <summary>
        /// Get the transform associated with the ICanvasElement.
        /// </summary>
        /// // 获取ICanvasElement关联的变换组件
        Transform transform { get; }

        /// <summary>
        /// Callback sent when this ICanvasElement has completed layout.
        /// </summary>
        ///  // 布局重建完成的回调函数
        void LayoutComplete();

        /// <summary>
        /// Callback sent when this ICanvasElement has completed Graphic rebuild.
        /// </summary>
        ///  // 图形重建完成的回调函数
        void GraphicUpdateComplete();

        /// <summary>


        /// Used if the native representation has been destroyed.
        /// </summary>
        /// <returns>Return true if the element is considered destroyed.</returns>
        ///  // 是否被销毁
        bool IsDestroyed();
    }

    /// <summary>
    /// A place where CanvasElements can register themselves for rebuilding.
    /// </summary>
    public class CanvasUpdateRegistry
    {
        private static CanvasUpdateRegistry s_Instance;

        private bool m_PerformingLayoutUpdate;
        private bool m_PerformingGraphicUpdate;

        //布局重建队列，当UI元素的布局需要更新时将其加入队列
        private readonly IndexedSet<ICanvasElement> m_LayoutRebuildQueue = new IndexedSet<ICanvasElement>();

        //图形重建队列，当UI元素的图像需要更新时将其加入队列
        private readonly IndexedSet<ICanvasElement> m_GraphicRebuildQueue = new IndexedSet<ICanvasElement>();

        protected CanvasUpdateRegistry()
        {
            //监听了Canvas的willRenderCanvases事件，这个事件会在渲染前进行每帧调用
            Canvas.willRenderCanvases += PerformUpdate;
        }

        /// <summary>
        /// Get the singleton registry instance.
        /// </summary>
        public static CanvasUpdateRegistry instance
        {
            get
            {
                if (s_Instance == null)
                    s_Instance = new CanvasUpdateRegistry();
                return s_Instance;
            }
        }

        private bool ObjectValidForUpdate(ICanvasElement element)
        {
            var valid = element != null;

            var isUnityObject = element is Object;
            if (isUnityObject)
                valid = (element as Object) != null; //Here we make use of the overloaded UnityEngine.Object == null, that checks if the native object is alive.

            return valid;
        }

        private void CleanInvalidItems()
        {
            // So MB's override the == operator for null equality, which checks
            // if they are destroyed. This is fine if you are looking at a concrete
            // mb, but in this case we are looking at a list of ICanvasElement
            // this won't forward the == operator to the MB, but just check if the
            // interface is null. IsDestroyed will return if the backend is destroyed.

            for (int i = m_LayoutRebuildQueue.Count - 1; i >= 0; --i)
            {
                var item = m_LayoutRebuildQueue[i];
                if (item == null)
                {
                    m_LayoutRebuildQueue.RemoveAt(i);
                    continue;
                }

                if (item.IsDestroyed())
                {
                    m_LayoutRebuildQueue.RemoveAt(i);
                    item.LayoutComplete();
                }
            }

            for (int i = m_GraphicRebuildQueue.Count - 1; i >= 0; --i)
            {
                var item = m_GraphicRebuildQueue[i];
                if (item == null)
                {
                    m_GraphicRebuildQueue.RemoveAt(i);
                    continue;
                }

                if (item.IsDestroyed())
                {
                    m_GraphicRebuildQueue.RemoveAt(i);
                    item.GraphicUpdateComplete();
                }
            }
        }

        private static readonly Comparison<ICanvasElement> s_SortLayoutFunction = SortLayoutList;
        private void PerformUpdate()
        {
            UISystemProfilerApi.BeginSample(UISystemProfilerApi.SampleType.Layout);
            //清理Queue中值为null或者被销毁的元素
            CleanInvalidItems();

            m_PerformingLayoutUpdate = true;

            //根据父节点多少排序(层级)
            m_LayoutRebuildQueue.Sort(s_SortLayoutFunction);//父节点越少在越前面
            for (int i = 0; i <= (int)CanvasUpdate.PostLayout; i++)
            {
                for (int j = 0; j < m_LayoutRebuildQueue.Count; j++)
                {
                    var rebuild = instance.m_LayoutRebuildQueue[j];
                    try
                    {
                        if (ObjectValidForUpdate(rebuild))
                        {
                            //布局重建，分别传入 Prelayout 、Layout 、PostLayout参数，布局的3个阶段
                            rebuild.Rebuild((CanvasUpdate)i);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e, rebuild.transform);
                    }
                }
            }

            //通知布局重建完成
            for (int i = 0; i < m_LayoutRebuildQueue.Count; ++i)
                m_LayoutRebuildQueue[i].LayoutComplete();

            instance.m_LayoutRebuildQueue.Clear();
            m_PerformingLayoutUpdate = false;

            // now layout is complete do culling...
            //执行裁剪(cull)操作
            ClipperRegistry.instance.Cull();

            m_PerformingGraphicUpdate = true;
            for (var i = (int)CanvasUpdate.PreRender; i < (int)CanvasUpdate.MaxUpdateValue; i++)
            {
                for (var k = 0; k < instance.m_GraphicRebuildQueue.Count; k++)
                {
                    try
                    {
                        var element = instance.m_GraphicRebuildQueue[k];
                        if (ObjectValidForUpdate(element))
                        {
                            //图形重建，分别传入PreRender、LatePreRender参数
                            element.Rebuild((CanvasUpdate)i);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e, instance.m_GraphicRebuildQueue[k].transform);
                    }
                }
            }
            //通知图形重建完成
            for (int i = 0; i < m_GraphicRebuildQueue.Count; ++i)
                m_GraphicRebuildQueue[i].GraphicUpdateComplete();

            instance.m_GraphicRebuildQueue.Clear();
            m_PerformingGraphicUpdate = false;
            UISystemProfilerApi.EndSample(UISystemProfilerApi.SampleType.Layout);
        }

        private static int ParentCount(Transform child)
        {
            if (child == null)
                return 0;

            var parent = child.parent;
            int count = 0;
            while (parent != null)
            {
                count++;
                parent = parent.parent;
            }
            return count;
        }

        private static int SortLayoutList(ICanvasElement x, ICanvasElement y)
        {
            Transform t1 = x.transform;
            Transform t2 = y.transform;

            return ParentCount(t1) - ParentCount(t2);
        }

        /// <summary>
        /// Try and add the given element to the layout rebuild list.
        /// Will not return if successfully added.
        /// </summary>
        /// <param name="element">The element that is needing rebuilt.</param>
        /// //向m_LayoutRebuildQueue中添加元素
        public static void RegisterCanvasElementForLayoutRebuild(ICanvasElement element)
        {
            instance.InternalRegisterCanvasElementForLayoutRebuild(element);
        }

        /// <summary>
        /// Try and add the given element to the layout rebuild list.
        /// </summary>
        /// <param name="element">The element that is needing rebuilt.</param>
        /// <returns>
        /// True if the element was successfully added to the rebuilt list.
        /// False if either already inside a Graphic Update loop OR has already been added to the list.
        /// </returns>
        /// //尝试向m_LayoutRebuildQueue中添加元素
        //并返回执行结果(True->成功， False->失败)
        public static bool TryRegisterCanvasElementForLayoutRebuild(ICanvasElement element)
        {
            return instance.InternalRegisterCanvasElementForLayoutRebuild(element);
        }

        //如果队列中不存在element元素，则添加
        private bool InternalRegisterCanvasElementForLayoutRebuild(ICanvasElement element)
        {
            if (m_LayoutRebuildQueue.Contains(element))
                return false;

            /* TODO: this likely should be here but causes the error to show just resizing the game view (case 739376)
            if (m_PerformingLayoutUpdate)
            {
                Debug.LogError(string.Format("Trying to add {0} for layout rebuild while we are already inside a layout rebuild loop. This is not supported.", element));
                return false;
            }*/

            return m_LayoutRebuildQueue.AddUnique(element);
        }

        /// <summary>
        /// Try and add the given element to the rebuild list.
        /// Will not return if successfully added.
        /// </summary>
        /// <param name="element">The element that is needing rebuilt.</param>
        /// //向m_GraphicRebuildQueue中添加元素
        public static void RegisterCanvasElementForGraphicRebuild(ICanvasElement element)
        {
            instance.InternalRegisterCanvasElementForGraphicRebuild(element);
        }

        /// <summary>
        /// Try and add the given element to the rebuild list.
        /// </summary>
        /// <param name="element">The element that is needing rebuilt.</param>
        /// <returns>
        /// True if the element was successfully added to the rebuilt list.
        /// False if either already inside a Graphic Update loop OR has already been added to the list.
        /// </returns>
        /// //尝试向m_GraphicRebuildQueue中添加元素
        public static bool TryRegisterCanvasElementForGraphicRebuild(ICanvasElement element)
        {
            return instance.InternalRegisterCanvasElementForGraphicRebuild(element);
        }

        private bool InternalRegisterCanvasElementForGraphicRebuild(ICanvasElement element)
        {
            if (m_PerformingGraphicUpdate)
            {
                Debug.LogError(string.Format("Trying to add {0} for graphic rebuild while we are already inside a graphic rebuild loop. This is not supported.", element));
                return false;
            }

            return m_GraphicRebuildQueue.AddUnique(element);
        }

        /// <summary>
        /// Remove the given element from both the graphic and the layout rebuild lists.
        /// </summary>
        /// <param name="element"></param>
        public static void UnRegisterCanvasElementForRebuild(ICanvasElement element)
        {
            instance.InternalUnRegisterCanvasElementForLayoutRebuild(element);
            instance.InternalUnRegisterCanvasElementForGraphicRebuild(element);
        }

        private void InternalUnRegisterCanvasElementForLayoutRebuild(ICanvasElement element)
        {
            if (m_PerformingLayoutUpdate)
            {
                Debug.LogError(string.Format("Trying to remove {0} from rebuild list while we are already inside a rebuild loop. This is not supported.", element));
                return;
            }

            element.LayoutComplete();
            instance.m_LayoutRebuildQueue.Remove(element);
        }

        private void InternalUnRegisterCanvasElementForGraphicRebuild(ICanvasElement element)
        {
            if (m_PerformingGraphicUpdate)
            {
                Debug.LogError(string.Format("Trying to remove {0} from rebuild list while we are already inside a rebuild loop. This is not supported.", element));
                return;
            }
            element.GraphicUpdateComplete();
            instance.m_GraphicRebuildQueue.Remove(element);
        }

        /// <summary>
        /// Are graphics layouts currently being calculated..
        /// </summary>
        /// <returns>True if the rebuild loop is CanvasUpdate.Prelayout, CanvasUpdate.Layout or CanvasUpdate.Postlayout</returns>
        public static bool IsRebuildingLayout()
        {
            return instance.m_PerformingLayoutUpdate;
        }

        /// <summary>
        /// Are graphics currently being rebuild.
        /// </summary>
        /// <returns>True if the rebuild loop is CanvasUpdate.PreRender or CanvasUpdate.Render</returns>
        public static bool IsRebuildingGraphics()
        {
            return instance.m_PerformingGraphicUpdate;
        }
    }
}
