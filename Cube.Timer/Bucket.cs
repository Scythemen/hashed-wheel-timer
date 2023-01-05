using System;
using System.Collections.Generic;

namespace Cube.Timer
{
    internal sealed class Bucket : IDisposable
    {
        // maintain the doubly linked list

        private BucketNode head;
        private BucketNode tail;

        public void AddNode(BucketNode node)
        {
            if (head == null)
            {
                head = tail = node;
            }
            else
            {// add to tail
                tail.Next = node;
                node.Prev = tail;
                tail = node;
            }
        }

        /// <summary>
        /// Remove the expired nodes, then add to unprocessed task queue.
        /// </summary>
        /// <param name="unprocessedTasks">the unprocessed task queue</param>
        /// <returns>the count of enqueued-task</returns>
        public int RemoveExpiredNodes(ref Queue<TimerTaskHandle> unprocessedTasks)
        {
            int count = 0;
            var node = head;
            while (node != null)
            {
                var next = node.Next;
                if (node.RemainingRounds <= 0)
                {
                    next = RemoveAndGetNext(node);
                    unprocessedTasks.Enqueue(node.TimerTaskHandle);
                    count++;
                }
                else
                {
                    node.DecreaseRemainingRounds();
                }

                node = next;
            }
            return count;
        }

        /// <summary>
        /// remove all the nodes of the bucket, then add to unprocessed task queue.
        /// </summary>
        /// <param name="unprocessedTasks">the unprocessed task queue</param>
        /// <returns>the count of enqueued-task</returns>
        public int RemoveAllNodes(ref Queue<TimerTaskHandle> unprocessedTasks)
        {
            int count = 0;
            var node = head;
            while (node != null)
            {
                var next = node.Next;

                unprocessedTasks.Enqueue(node.TimerTaskHandle);
                count++;

                node = next;
            }

            head = null;
            tail = null;
            return count;
        }

        // remove node from linked-list
        private BucketNode RemoveAndGetNext(BucketNode node)
        {
            if (node == head && node == tail)
            {
                head = null;
                tail = null;
                return null;
            }

            if (node == head)
            {
                head = node.Next;
                head.Prev = null;
                return node.Next;
            }

            if (node == tail)
            {
                tail = tail.Prev;
                tail.Next = null;
                return null;
            }

            node.Prev.Next = node.Next;
            node.Next.Prev = node.Prev;

            return node.Next;
        }

        public void Dispose()
        {
            head = null;
            tail = null;
        }

    }
}