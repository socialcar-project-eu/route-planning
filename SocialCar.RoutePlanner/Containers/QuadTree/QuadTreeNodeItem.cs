using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;

namespace SocialCar.RoutePlanner.Containers.QuadTree
{
    [Serializable]
    class QuadTreeNodeItem<T> : IHasRect
    {
        /// <summary>
        /// the default size of this item
        /// </summary>
        float m_size = (float)0.0001;

        /// <summary>
        /// Bounds of this item
        /// </summary>
        RectangleF m_rectangle;

        public T Content { get; private set; }

        /// <summary>
        /// Create an item at the given location with the given size.
        /// </summary>
        public QuadTreeNodeItem(T Node, float X, float Y, float size = 0)
        {
            m_size = size;
            Content = Node;
            m_rectangle = new RectangleF(X, Y, m_size, m_size);
        }

        #region IHasRect Members

        /// <summary>
        /// The rectangular bounds of this item
        /// </summary>
        public RectangleF Rectangle { get { return m_rectangle; } }

        #endregion
    }
}
