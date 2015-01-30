using System;
using System.Collections.Generic;
using System.Threading;

namespace Summoning
{
    internal class SingleLink<T>
    {
        public SingleLink<T> Next;
        public T Object;
    }
    public class LockFreeQueue<T> : IEnumerable<T>
    {

        private SingleLink<T> mHead;
        private SingleLink<T> mTail;
        private int mCount = 0;

        public int Count { get { return mCount; } }
        public LockFreeQueue()
        {
            mHead = new SingleLink<T>();
            mTail = mHead;
        }

        public LockFreeQueue(IEnumerable<T> objects)
            : this()
        {
            foreach (var obj in objects)
            {
                Enqueue(obj);
            }
        }

        public void Enqueue(T item)
        {
            SingleLink<T> oldTail = null;
            SingleLink<T> oldTailNext;

            var newNode = new SingleLink<T> { Object = item };

            bool newNodeWasAdded = false;

            while (!newNodeWasAdded)
            {
                oldTail = mTail;
                oldTailNext = oldTail.Next;

                if (mTail == oldTail)
                {
                    if (oldTailNext == null)
                    {
                        newNodeWasAdded =
                            Interlocked.CompareExchange<SingleLink<T>>(ref mTail.Next, newNode, null) == null;
                    }
                    else
                    {
                        Interlocked.CompareExchange<SingleLink<T>>(ref mTail, oldTailNext, oldTail);
                    }
                }
            }

            Interlocked.CompareExchange<SingleLink<T>>(ref mTail, newNode, oldTail);
            Interlocked.Increment(ref mCount);
        }

        public T TryDequeue()
        {
            T item;
            TryDequeue(out item);
            return item;
        }

        public bool TryDequeue(out T item)
        {
            item = default(T);
            SingleLink<T> oldHead = null;

            bool haveAdvancedHead = false;
            while (!haveAdvancedHead)
            {
                oldHead = mHead;
                SingleLink<T> oldTail = mTail;
                SingleLink<T> oldHeadNext = oldHead.Next;

                if (oldHead == mHead)
                {
                    if (oldHead == oldTail)
                    {
                        if (oldHeadNext == null)
                            return false;

                        Interlocked.CompareExchange(ref mTail, oldHeadNext, oldTail);
                    }

                    else
                    {
                        item = oldHeadNext.Object;
                        haveAdvancedHead =
                          Interlocked.CompareExchange(ref mHead, oldHeadNext, oldHead) == oldHead;
                    }
                }
            }

            Interlocked.Decrement(ref mCount);
            return true;
        }

        public T Dequeue()
        {
            T result;

            if (!TryDequeue(out result))
            {
                throw new InvalidOperationException("the queue is empty");
            }

            return result;
        }

        public IEnumerator<T> GetEnumerator()
        {
            SingleLink<T> currentNode = mHead;

            do
            {
                if (currentNode.Object == null)
                {
                    yield break;
                }
                else
                {
                    yield return currentNode.Object;
                }
            }
            while ((currentNode = currentNode.Next) != null);

            yield break;
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void Clear()
        {
            SingleLink<T> tempNode;
            SingleLink<T> currentNode = mHead;

            while (currentNode != null)
            {
                tempNode = currentNode;
                currentNode = currentNode.Next;

                tempNode.Object = default(T);
                tempNode.Next = null;
            }

            mHead = new SingleLink<T>();
            mTail = mHead;
            mCount = 0;
        }
    }
}